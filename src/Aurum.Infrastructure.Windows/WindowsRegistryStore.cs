using System.Globalization;
using System.Text.Json;
using Aurum.Core;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsRegistryStore : ISystemStore
{
    public Task<RegistrySnapshot> ReadRegistryAsync(
        RegistryTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);

        using var baseKey = OpenBaseKey(target.Hive);
        using var key = baseKey.OpenSubKey(target.SubKey, writable: false);

        if (key is null || !key.GetValueNames().Contains(target.ValueName, StringComparer.OrdinalIgnoreCase))
        {
            return Task.FromResult(RegistrySnapshot.Missing);
        }

        var rawValue = key.GetValue(target.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new InvalidOperationException($"Registry value '{target.DisplayPath}' unexpectedly returned null.");
        var valueKind = key.GetValueKind(target.ValueName);

        return Task.FromResult(new RegistrySnapshot(true, SerializeValue(rawValue, valueKind)));
    }

    public Task WriteRegistryAsync(
        RegistryTarget target,
        RegistryValue value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(value);

        using var baseKey = OpenBaseKey(target.Hive);
        using var key = baseKey.CreateSubKey(target.SubKey, writable: true)
            ?? throw new UnauthorizedAccessException($"Could not open '{target.DisplayPath}' for writing.");

        var (rawValue, valueKind) = DeserializeValue(value);
        key.SetValue(target.ValueName, rawValue, valueKind);
        return Task.CompletedTask;
    }

    public Task DeleteRegistryValueAsync(
        RegistryTarget target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(target);

        using var baseKey = OpenBaseKey(target.Hive);
        using var key = baseKey.OpenSubKey(target.SubKey, writable: true);
        key?.DeleteValue(target.ValueName, throwOnMissingValue: false);
        return Task.CompletedTask;
    }

    private static RegistryKey OpenBaseKey(RegistryHiveId hive) =>
        RegistryKey.OpenBaseKey(
            hive switch
            {
                RegistryHiveId.CurrentUser => RegistryHive.CurrentUser,
                RegistryHiveId.LocalMachine => RegistryHive.LocalMachine,
                _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, null)
            },
            RegistryView.Default);

    private static RegistryValue SerializeValue(object rawValue, RegistryValueKind valueKind) =>
        valueKind switch
        {
            RegistryValueKind.String => new RegistryValue((string)rawValue, RegistryValueType.String),
            RegistryValueKind.ExpandString => new RegistryValue((string)rawValue, RegistryValueType.ExpandString),
            RegistryValueKind.DWord => new RegistryValue(
                Convert.ToInt32(rawValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueType.DWord),
            RegistryValueKind.QWord => new RegistryValue(
                Convert.ToInt64(rawValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                RegistryValueType.QWord),
            RegistryValueKind.MultiString => new RegistryValue(
                JsonSerializer.Serialize((string[])rawValue),
                RegistryValueType.MultiString),
            RegistryValueKind.Binary => new RegistryValue(
                Convert.ToBase64String((byte[])rawValue),
                RegistryValueType.Binary),
            _ => throw new NotSupportedException($"Registry value kind '{valueKind}' is not supported.")
        };

    private static (object RawValue, RegistryValueKind ValueKind) DeserializeValue(RegistryValue value) =>
        value.Type switch
        {
            RegistryValueType.String => (value.Data, RegistryValueKind.String),
            RegistryValueType.ExpandString => (value.Data, RegistryValueKind.ExpandString),
            RegistryValueType.DWord => (
                int.Parse(value.Data, NumberStyles.Integer, CultureInfo.InvariantCulture),
                RegistryValueKind.DWord),
            RegistryValueType.QWord => (
                long.Parse(value.Data, NumberStyles.Integer, CultureInfo.InvariantCulture),
                RegistryValueKind.QWord),
            RegistryValueType.MultiString => (
                JsonSerializer.Deserialize<string[]>(value.Data)
                    ?? throw new InvalidOperationException("A multi-string value could not be deserialized."),
                RegistryValueKind.MultiString),
            RegistryValueType.Binary => (Convert.FromBase64String(value.Data), RegistryValueKind.Binary),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value.Type, null)
        };
}
