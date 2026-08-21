using System.Collections.ObjectModel;
using System.Windows.Threading;
using Aurum.Infrastructure.Windows;

namespace Aurum.App.ViewModels;

public sealed class MonitoringViewModel : ObservableObject, IDisposable
{
    private const int HistoryLength = 60;
    private readonly HardwareMonitorService _monitor;
    private readonly DispatcherTimer _timer;
    private bool _isSampling;
    private bool _disposed;
    private string _cpuName = "Определение процессора…";
    private string _cpuDetails = "Сведения загружаются";
    private string _gpuName = "Определение видеоадаптера…";
    private string _gpuDetails = "Сведения загружаются";
    private string _memoryDetails = "Сведения загружаются";
    private string _driveName = "Определение системного диска…";
    private string _driveDetails = "Сведения загружаются";
    private string _networkName = "Определение подключения…";
    private string _networkDetails = "Сведения загружаются";
    private string _powerPlanName = "Определение…";
    private string _cpuUsage = "—";
    private string _gpuUsage = "—";
    private string _memoryUsage = "—";
    private string _memoryUsageDetails = "Ожидание первого замера";
    private string _diskUsage = "—";
    private string _diskActivity = "Ожидание первого замера";
    private string _networkActivity = "Ожидание первого замера";
    private string _uptime = "—";
    private string _lastSample = "Показатели обновляются раз в секунду";
    private ulong _totalMemoryBytes;

    public MonitoringViewModel(HardwareMonitorService monitor)
    {
        _monitor = monitor;
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _timer.Tick += OnTimerTick;
    }

    public ObservableCollection<double> CpuHistory { get; } = [];
    public ObservableCollection<double> GpuHistory { get; } = [];
    public ObservableCollection<double> MemoryHistory { get; } = [];

    public string CpuName { get => _cpuName; private set => SetProperty(ref _cpuName, value); }
    public string CpuDetails { get => _cpuDetails; private set => SetProperty(ref _cpuDetails, value); }
    public string GpuName { get => _gpuName; private set => SetProperty(ref _gpuName, value); }
    public string GpuDetails { get => _gpuDetails; private set => SetProperty(ref _gpuDetails, value); }
    public string MemoryDetails { get => _memoryDetails; private set => SetProperty(ref _memoryDetails, value); }
    public string DriveName { get => _driveName; private set => SetProperty(ref _driveName, value); }
    public string DriveDetails { get => _driveDetails; private set => SetProperty(ref _driveDetails, value); }
    public string NetworkName { get => _networkName; private set => SetProperty(ref _networkName, value); }
    public string NetworkDetails { get => _networkDetails; private set => SetProperty(ref _networkDetails, value); }
    public string PowerPlanName { get => _powerPlanName; private set => SetProperty(ref _powerPlanName, value); }
    public string CpuUsage { get => _cpuUsage; private set => SetProperty(ref _cpuUsage, value); }
    public string GpuUsage { get => _gpuUsage; private set => SetProperty(ref _gpuUsage, value); }
    public string MemoryUsage { get => _memoryUsage; private set => SetProperty(ref _memoryUsage, value); }
    public string MemoryUsageDetails { get => _memoryUsageDetails; private set => SetProperty(ref _memoryUsageDetails, value); }
    public string DiskUsage { get => _diskUsage; private set => SetProperty(ref _diskUsage, value); }
    public string DiskActivity { get => _diskActivity; private set => SetProperty(ref _diskActivity, value); }
    public string NetworkActivity { get => _networkActivity; private set => SetProperty(ref _networkActivity, value); }
    public string Uptime { get => _uptime; private set => SetProperty(ref _uptime, value); }
    public string LastSample { get => _lastSample; private set => SetProperty(ref _lastSample, value); }

    public async Task InitializeAsync()
    {
        var inventory = await _monitor.CaptureInventoryAsync();
        CpuName = inventory.CpuName;
        CpuDetails = inventory.CpuDetails;
        GpuName = inventory.GpuName;
        GpuDetails = inventory.GpuDetails;
        _totalMemoryBytes = inventory.TotalMemoryBytes;
        MemoryDetails = inventory.MemoryDetails;
        DriveName = inventory.SystemDriveName;
        DriveDetails = inventory.SystemDriveDetails;
        DiskUsage = FormatPercent(inventory.SystemDriveUsedPercent);
        DiskActivity = $"Свободно {FormatBytes(inventory.SystemDriveFreeBytes)} · снимок при открытии";
        NetworkName = inventory.NetworkName;
        NetworkDetails = inventory.NetworkDetails;
        PowerPlanName = inventory.PowerPlanName;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_timer.IsEnabled)
        {
            return;
        }

        _timer.Start();
        _ = SampleAsync();
    }

    public void Stop() => _timer.Stop();

    public void SetPowerPlanName(string name) => PowerPlanName = name;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _monitor.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        try
        {
            await SampleAsync();
        }
        catch (Exception error)
        {
            LastSample = $"Ошибка мониторинга: {error.Message}";
        }
    }

    private async Task SampleAsync()
    {
        if (_isSampling || _disposed)
        {
            return;
        }

        _isSampling = true;
        try
        {
            var sample = await _monitor.SampleAsync();
            CpuUsage = FormatPercent(sample.CpuUsagePercent);
            GpuUsage = sample.GpuUsagePercent is null ? "—" : FormatPercent(sample.GpuUsagePercent.Value);
            MemoryUsage = FormatPercent(sample.MemoryUsagePercent);
            MemoryUsageDetails = $"{FormatBytes(sample.UsedMemoryBytes)} из {FormatBytes(_totalMemoryBytes)}";
            NetworkActivity = $"↓ {FormatBytesPerSecond(sample.NetworkReceiveBytesPerSecond)}   ↑ {FormatBytesPerSecond(sample.NetworkSendBytesPerSecond)}";
            Uptime = FormatUptime(sample.Uptime);
            LastSample = $"Обновлено {sample.SampledAt:HH:mm:ss} · интервал 1 секунда";

            Push(CpuHistory, sample.CpuUsagePercent);
            Push(GpuHistory, sample.GpuUsagePercent ?? 0);
            Push(MemoryHistory, sample.MemoryUsagePercent);
        }
        catch (Exception error)
        {
            LastSample = $"Не удалось обновить показатели: {error.Message}";
        }
        finally
        {
            _isSampling = false;
        }
    }

    private static void Push(ObservableCollection<double> history, double value)
    {
        history.Add(Math.Clamp(value, 0, 100));
        while (history.Count > HistoryLength)
        {
            history.RemoveAt(0);
        }
    }

    private static string FormatPercent(double value) => $"{Math.Clamp(value, 0, 100):0}%";

    private static string FormatBytesPerSecond(double bytes) => $"{FormatBytes((ulong)Math.Max(0, bytes))}/с";

    private static string FormatBytes(ulong bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    private static string FormatUptime(TimeSpan uptime) => uptime.TotalDays >= 1
        ? $"{(int)uptime.TotalDays} д {uptime.Hours:00} ч {uptime.Minutes:00} мин"
        : $"{uptime.Hours:00} ч {uptime.Minutes:00} мин";
}
