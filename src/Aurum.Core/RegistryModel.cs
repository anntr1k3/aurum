namespace Aurum.Core;

public enum RegistryHiveId
{
    CurrentUser,
    LocalMachine
}

public enum RegistryValueType
{
    String,
    ExpandString,
    DWord,
    QWord,
    MultiString,
    Binary
}

public sealed record RegistryTarget
{
    public RegistryTarget(RegistryHiveId hive, string subKey, string valueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
        ArgumentNullException.ThrowIfNull(valueName);

        Hive = hive;
        SubKey = subKey.Trim().TrimStart('\\');
        ValueName = valueName;
    }

    public RegistryHiveId Hive { get; }

    public string SubKey { get; }

    public string ValueName { get; }

    public string DisplayPath =>
        $"{(Hive == RegistryHiveId.CurrentUser ? "HKCU" : "HKLM")}\\{SubKey}\\{ValueName}";
}

public sealed record RegistryValue(string Data, RegistryValueType Type)
{
    public static RegistryValue DWord(int value) =>
        new(value.ToString(System.Globalization.CultureInfo.InvariantCulture), RegistryValueType.DWord);

    public static RegistryValue String(string value) =>
        new(value, RegistryValueType.String);
}

public sealed record RegistrySnapshot(bool Exists, RegistryValue? Value)
{
    public static RegistrySnapshot Missing { get; } = new(false, null);
}

public sealed record RegistryMutation(RegistryTarget Target, RegistryValue DesiredValue);
