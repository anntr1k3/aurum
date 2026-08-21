using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Aurum.Core;
using Microsoft.Win32;

namespace Aurum.Infrastructure.Windows;

public sealed class WindowsSystemTimerService : ISystemTimerService, IDisposable
{
    private const string KernelSessionManagerPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel";
    private const string GlobalTimerResolutionValue = "GlobalTimerResolutionRequests";

    private bool _isCustomResolutionActive;
    private uint _activeRequestedResolution;

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtQueryTimerResolution(
        out uint minimumResolution,
        out uint maximumResolution,
        out uint currentResolution);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetTimerResolution(
        uint desiredResolution,
        bool setResolution,
        out uint currentResolution);

    public TimerResolutionInfo GetResolution()
    {
        try
        {
            var status = NtQueryTimerResolution(out var minRes, out var maxRes, out var curRes);
            if (status == 0)
            {
                return new TimerResolutionInfo(
                    MinimumResolution100Ns: minRes,
                    MaximumResolution100Ns: maxRes,
                    CurrentResolution100Ns: curRes,
                    IsCustomResolutionActive: _isCustomResolutionActive);
            }
        }
        catch
        {
            // Fallback
        }

        // Fallback: 0.5ms (5000) min, 15.625ms (156250) max, 15.625ms cur
        return new TimerResolutionInfo(5000, 156250, 156250, _isCustomResolutionActive);
    }

    public bool SetResolution(double desiredMilliseconds)
    {
        if (desiredMilliseconds <= 0)
        {
            return ResetResolution();
        }

        // Переводим миллисекунды в единицы по 100 нс (1 мс = 10,000 единиц)
        var desired100Ns = (uint)Math.Round(desiredMilliseconds * 10000.0);
        if (desired100Ns < 5000) // Мин 0.5 мс
        {
            desired100Ns = 5000;
        }

        try
        {
            var status = NtSetTimerResolution(desired100Ns, setResolution: true, out var currentRes);
            if (status == 0)
            {
                _isCustomResolutionActive = true;
                _activeRequestedResolution = desired100Ns;
                return true;
            }
        }
        catch
        {
            // Игнорируем
        }

        return false;
    }

    public bool ResetResolution()
    {
        try
        {
            var status = NtSetTimerResolution(0, setResolution: false, out _);
            _isCustomResolutionActive = false;
            _activeRequestedResolution = 0;
            return status == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool IsGlobalResolutionPolicyEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(KernelSessionManagerPath, writable: false);
            if (key is not null && key.GetValue(GlobalTimerResolutionValue) is int val)
            {
                return val == 1;
            }
        }
        catch
        {
            // Игнорируем
        }

        return false;
    }

    public Task SetGlobalResolutionPolicyAsync(bool enable, CancellationToken cancellationToken = default)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(KernelSessionManagerPath, writable: true);
            key.SetValue(GlobalTimerResolutionValue, enable ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Не удалось изменить политику GlobalTimerResolutionRequests: {ex.Message}", ex);
        }

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_isCustomResolutionActive)
        {
            ResetResolution();
        }
    }
}
