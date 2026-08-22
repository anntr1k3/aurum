namespace Aurum.Core;

public sealed record ProcessorTopologyInfo(
    int LogicalProcessorCount,
    int CoreCount,
    int EfficiencyClassCount,
    bool IsHeterogeneous);

public interface IProcessorTopology
{
    ProcessorTopologyInfo Capture();
}

public static class CoreParkingGuidance
{
    public static bool IsBlanketUnpark(CoreParkingSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.MinimumAc >= 100 && settings.MaximumAc >= 100 &&
               settings.MinimumDc >= 100 && settings.MaximumDc >= 100;
    }

    public const string HeterogeneousUnparkWarning =
        "На этом процессоре есть ядра разной эффективности (P/E). Полное распарковывание может помешать планировщику Windows, терморежиму и работе от батареи.";
}
