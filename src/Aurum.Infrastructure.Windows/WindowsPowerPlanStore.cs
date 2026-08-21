using System.ComponentModel;
using System.Runtime.InteropServices;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsPowerPlanStore : IPowerPlanStore
{
    private const uint AccessScheme = 16;
    private const uint ErrorNoMoreItems = 259;

    public Task<PowerPlanSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activePlanId = GetActivePlanId();
        var plans = EnumeratePlans(cancellationToken)
            .OrderByDescending(plan => plan.Id == activePlanId)
            .ThenBy(static plan => plan.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return Task.FromResult(new PowerPlanSnapshot(plans, activePlanId));
    }

    public Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = PowerPlanNativeMethods.PowerSetActiveScheme(IntPtr.Zero, ref planId);
        if (result != 0)
        {
            throw new Win32Exception((int)result, $"Windows could not activate power plan '{planId}'.");
        }

        return Task.CompletedTask;
    }

    private static IReadOnlyList<PowerPlanInfo> EnumeratePlans(CancellationToken cancellationToken)
    {
        var plans = new List<PowerPlanInfo>();
        for (uint index = 0; ; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var guidBytes = new byte[16];
            uint size = (uint)guidBytes.Length;
            var result = PowerPlanNativeMethods.PowerEnumerate(
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero,
                AccessScheme,
                index,
                guidBytes,
                ref size);
            if (result == ErrorNoMoreItems)
            {
                break;
            }

            if (result != 0)
            {
                throw new Win32Exception((int)result, "Windows could not enumerate power plans.");
            }

            var planId = new Guid(guidBytes);
            var name = ReadFriendlyName(planId) ?? $"План {planId:D}";
            plans.Add(new PowerPlanInfo(planId, name));
        }

        return plans;
    }

    private static Guid GetActivePlanId()
    {
        var result = PowerPlanNativeMethods.PowerGetActiveScheme(IntPtr.Zero, out var guidPointer);
        if (result != 0 || guidPointer == IntPtr.Zero)
        {
            throw new Win32Exception((int)result, "Windows did not return the active power plan.");
        }

        try
        {
            return Marshal.PtrToStructure<Guid>(guidPointer);
        }
        finally
        {
            _ = PowerPlanNativeMethods.LocalFree(guidPointer);
        }
    }

    private static string? ReadFriendlyName(Guid planId)
    {
        uint bufferSize = 0;
        _ = PowerPlanNativeMethods.PowerReadFriendlyName(
            IntPtr.Zero,
            ref planId,
            IntPtr.Zero,
            IntPtr.Zero,
            IntPtr.Zero,
            ref bufferSize);
        if (bufferSize == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            var result = PowerPlanNativeMethods.PowerReadFriendlyName(
                IntPtr.Zero,
                ref planId,
                IntPtr.Zero,
                IntPtr.Zero,
                buffer,
                ref bufferSize);
            return result == 0 ? Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

internal static class PowerPlanNativeMethods
{
    [DllImport("powrprof.dll")]
    internal static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerSetActiveScheme(IntPtr userRootPowerKey, ref Guid schemeGuid);

    [DllImport("powrprof.dll")]
    internal static extern uint PowerEnumerate(
        IntPtr rootPowerKey,
        IntPtr schemeGuid,
        IntPtr subgroupOfPowerSettingsGuid,
        uint accessFlags,
        uint index,
        [Out] byte[] buffer,
        ref uint bufferSize);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    internal static extern uint PowerReadFriendlyName(
        IntPtr rootPowerKey,
        ref Guid schemeGuid,
        IntPtr subgroupOfPowerSettingsGuid,
        IntPtr powerSettingGuid,
        IntPtr buffer,
        ref uint bufferSize);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr LocalFree(IntPtr memory);
}
