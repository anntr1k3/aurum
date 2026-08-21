using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsCoreParkingStore : ICoreParkingStore
{
    private static readonly Guid ProcessorSubgroup = new("54533251-82be-4824-96c1-47b60b740d00");
    private static readonly Guid MinimumCores = new("0cc5b647-c1df-4637-891a-dec35c318583");
    private static readonly Guid MaximumCores = new("ea062031-0e34-4ff1-9b6d-eb1059334028");
    private readonly WindowsPowerPlanStore _plans = new();

    public async Task<CoreParkingPlan> CaptureActiveAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _plans.CaptureAsync(cancellationToken);
        var active = snapshot.Plans.FirstOrDefault(plan => plan.Id == snapshot.ActivePlanId)
            ?? new PowerPlanInfo(snapshot.ActivePlanId, snapshot.ActivePlanId.ToString("D"));
        return new CoreParkingPlan(active.Id, active.Name, await ReadSettingsAsync(active.Id, cancellationToken));
    }

    public Task<Guid> DuplicateAsync(Guid sourcePlanId, string newName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = CoreParkingNativeMethods.PowerDuplicateScheme(IntPtr.Zero, ref sourcePlanId, out var targetPointer);
        if (result != 0 || targetPointer == IntPtr.Zero)
        {
            throw new Win32Exception((int)result, "Windows не смогла создать копию активной схемы питания.");
        }

        Guid targetId;
        try { targetId = Marshal.PtrToStructure<Guid>(targetPointer); }
        finally { _ = CoreParkingNativeMethods.LocalFree(targetPointer); }

        var nameBytes = Encoding.Unicode.GetBytes(newName + '\0');
        result = CoreParkingNativeMethods.PowerWriteFriendlyName(
            IntPtr.Zero, ref targetId, IntPtr.Zero, IntPtr.Zero, nameBytes, (uint)nameBytes.Length);
        if (result != 0)
        {
            _ = CoreParkingNativeMethods.PowerDeleteScheme(IntPtr.Zero, ref targetId);
            throw new Win32Exception((int)result, "Windows создала копию схемы, но не смогла присвоить ей имя.");
        }

        return Task.FromResult(targetId);
    }

    public Task<CoreParkingSettings> ReadSettingsAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subgroup = ProcessorSubgroup;
        var minimum = MinimumCores;
        var maximum = MaximumCores;
        Check(CoreParkingNativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref minimum, out var minimumAc), "чтение минимума ядер от сети");
        Check(CoreParkingNativeMethods.PowerReadACValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref maximum, out var maximumAc), "чтение максимума ядер от сети");
        Check(CoreParkingNativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref minimum, out var minimumDc), "чтение минимума ядер от батареи");
        Check(CoreParkingNativeMethods.PowerReadDCValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref maximum, out var maximumDc), "чтение максимума ядер от батареи");
        return Task.FromResult(new CoreParkingSettings(minimumAc, maximumAc, minimumDc, maximumDc));
    }

    public Task WriteSettingsAsync(Guid planId, CoreParkingSettings settings, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        settings.Validate();
        var subgroup = ProcessorSubgroup;
        var minimum = MinimumCores;
        var maximum = MaximumCores;
        Check(CoreParkingNativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref minimum, settings.MinimumAc), "запись минимума ядер от сети");
        Check(CoreParkingNativeMethods.PowerWriteACValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref maximum, settings.MaximumAc), "запись максимума ядер от сети");
        Check(CoreParkingNativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref minimum, settings.MinimumDc), "запись минимума ядер от батареи");
        Check(CoreParkingNativeMethods.PowerWriteDCValueIndex(IntPtr.Zero, ref planId, ref subgroup, ref maximum, settings.MaximumDc), "запись максимума ядер от батареи");
        return Task.CompletedTask;
    }

    public Task SetActiveAsync(Guid planId, CancellationToken cancellationToken = default) =>
        _plans.SetActiveAsync(planId, cancellationToken);

    public async Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) =>
        (await _plans.CaptureAsync(cancellationToken)).Plans.Any(plan => plan.Id == planId);

    public Task DeleteAsync(Guid planId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Check(CoreParkingNativeMethods.PowerDeleteScheme(IntPtr.Zero, ref planId), "удаление схемы питания Aurum");
        return Task.CompletedTask;
    }

    private static void Check(uint result, string operation)
    {
        if (result != 0) throw new Win32Exception((int)result, $"Windows не смогла выполнить операцию: {operation}.");
    }
}

internal static class CoreParkingNativeMethods
{
    [DllImport("powrprof.dll")] internal static extern uint PowerDuplicateScheme(IntPtr root, ref Guid source, out IntPtr target);
    [DllImport("powrprof.dll")] internal static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)] internal static extern uint PowerWriteFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[] buffer, uint size);
    [DllImport("powrprof.dll")] internal static extern uint PowerReadACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] internal static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] internal static extern uint PowerWriteACValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("powrprof.dll")] internal static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid subgroup, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] internal static extern IntPtr LocalFree(IntPtr memory);
}
