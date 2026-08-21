using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aurum.Core;

public sealed record TimerResolutionInfo(
    uint MinimumResolution100Ns,
    uint MaximumResolution100Ns,
    uint CurrentResolution100Ns,
    bool IsCustomResolutionActive)
{
    public double MinimumMs => MinimumResolution100Ns / 10000.0;
    public double MaximumMs => MaximumResolution100Ns / 10000.0;
    public double CurrentMs => CurrentResolution100Ns / 10000.0;
    public double FrequencyHz => CurrentMs > 0 ? 1000.0 / CurrentMs : 0;

    public string FormattedCurrent => $"{CurrentMs:0.000} мс ({FrequencyHz:0} Гц)";
}

public interface ISystemTimerService
{
    TimerResolutionInfo GetResolution();
    bool SetResolution(double desiredMilliseconds);
    void ResetResolution();
    bool IsGlobalResolutionPolicyEnabled();
    Task SetGlobalResolutionPolicyAsync(bool enable, CancellationToken cancellationToken = default);
}
