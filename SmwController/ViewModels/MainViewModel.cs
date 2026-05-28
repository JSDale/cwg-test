using SmwController.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmwController.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISmwService _smwService;

    private string _ipAddress = string.Empty;
    private int _port = 5025;
    private string _scanRootPath = "/var";
    private string _searchText = string.Empty;
    private string? _selectedWaveform;
    private string _status = "Not connected.";
    private double _currentLevel = -60.0;
    private bool _isBusy;
    private bool _isRfActive;
    private CancellationTokenSource? _rampCts;

    public const double StartLevel = -60.0;
    public const double StopLevel = 0.0;
    public const double StepSize = 10.0;
    public const int DwellSeconds = 10;

    public MainViewModel(ISmwService smwService)
    {
        _smwService = smwService;

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        DisconnectCommand = new RelayCommand(Disconnect,
            () => _smwService.IsConnected && !IsBusy);
        ScanWaveformsCommand = new AsyncRelayCommand(ScanWaveformsAsync,
            () => _smwService.IsConnected && !IsBusy && !IsRfActive);
        LoadWaveformCommand = new AsyncRelayCommand(LoadWaveformAsync,
            () => _smwService.IsConnected && SelectedWaveform != null && !IsBusy && !IsRfActive);
        StartRfCommand = new AsyncRelayCommand(StartRfAsync,
            () => _smwService.IsConnected && !IsRfActive && !IsBusy);
        StopRfCommand = new RelayCommand(StopRf, () => IsRfActive);
    }

    // ── Connection ───────────────────────────────────────────────────────────

    public string IpAddress
    {
        get => _ipAddress;
        set => SetProperty(ref _ipAddress, value);
    }

    public int Port
    {
        get => _port;
        set => SetProperty(ref _port, value);
    }

    public bool IsConnected => _smwService.IsConnected;

    // ── Waveform browser ─────────────────────────────────────────────────────

    public string ScanRootPath
    {
        get => _scanRootPath;
        set => SetProperty(ref _scanRootPath, value);
    }

    /// <summary>All waveform paths returned by the last scan.</summary>
    public ObservableCollection<string> WaveformFiles { get; } = new();

    /// <summary>Live-filtered view of <see cref="WaveformFiles"/>.</summary>
    public ObservableCollection<string> FilteredWaveformFiles { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    public string? SelectedWaveform
    {
        get => _selectedWaveform;
        set => SetProperty(ref _selectedWaveform, value);
    }

    // ── RF control ───────────────────────────────────────────────────────────

    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public double CurrentLevel
    {
        get => _currentLevel;
        set => SetProperty(ref _currentLevel, value);
    }

    /// <summary>True only during short blocking operations (connect, scan, load waveform).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>True while the RF ramp is running or RF is held at the final level.</summary>
    public bool IsRfActive
    {
        get => _isRfActive;
        private set => SetProperty(ref _isRfActive, value);
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand ScanWaveformsCommand { get; }
    public AsyncRelayCommand LoadWaveformCommand { get; }
    public AsyncRelayCommand StartRfCommand { get; }
    public RelayCommand StopRfCommand { get; }

    // ── Implementations ───────────────────────────────────────────────────────

    private async Task ConnectAsync(CancellationToken ct)
    {
        IsBusy = true;
        Status = $"Connecting to {IpAddress}:{Port}…";
        try
        {
            await _smwService.ConnectAsync(IpAddress, Port, ct);
            Status = "Connected.";
        }
        catch (Exception ex)
        {
            Status = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsConnected));
        }
    }

    private void Disconnect()
    {
        StopRf();
        _smwService.Disconnect();
        IsRfActive = false;
        Status = "Disconnected.";
        OnPropertyChanged(nameof(IsConnected));
    }

    private async Task ScanWaveformsAsync(CancellationToken ct)
    {
        IsBusy = true;
        Status = $"Scanning {ScanRootPath} for waveform files…";
        WaveformFiles.Clear();
        FilteredWaveformFiles.Clear();
        SelectedWaveform = null;

        try
        {
            var files = await _smwService.GetWaveformFilesAsync(ScanRootPath, ct);
            foreach (var f in files)
                WaveformFiles.Add(f);

            ApplyFilter();
            Status = $"Found {files.Count} waveform file{(files.Count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadWaveformAsync(CancellationToken ct)
    {
        if (SelectedWaveform is null) return;

        IsBusy = true;
        Status = $"Loading waveform: {SelectedWaveform}…";
        try
        {
            await _smwService.SelectWaveformAsync(SelectedWaveform, ct);
            Status = $"Waveform loaded. ARB generator enabled.";
        }
        catch (Exception ex)
        {
            Status = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartRfAsync(CancellationToken ct)
    {
        _rampCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = _rampCts.Token;

        IsRfActive = true;
        CurrentLevel = StartLevel;

        try
        {
            await _smwService.SetPowerLevelAsync(StartLevel, token);
            await _smwService.StartRfOutputAsync(token);

            double level = StartLevel;
            while (!token.IsCancellationRequested)
            {
                await _smwService.SetPowerLevelAsync(level, token);
                CurrentLevel = level;
                Status = $"RF ON — Level: {level:+0.0;-0.0} dBm";

                if (level >= StopLevel)
                {
                    Status = $"RF ON — Ramp complete. Holding at {level:+0.0;-0.0} dBm. Press Stop to disable RF.";
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }

                await Task.Delay(TimeSpan.FromSeconds(DwellSeconds), token);
                level += StepSize;
            }
        }
        catch (OperationCanceledException)
        {
            Status = $"RF stopped at {CurrentLevel:+0.0;-0.0} dBm.";
        }
        catch (Exception ex)
        {
            Status = $"RF error: {ex.Message}";
        }
        finally
        {
            try { await _smwService.StopRfOutputAsync(CancellationToken.None); }
            catch { /* best-effort */ }

            IsRfActive = false;
            _rampCts?.Dispose();
            _rampCts = null;
        }
    }

    private void StopRf() => _rampCts?.Cancel();

    // ── Filtering ─────────────────────────────────────────────────────────────

    private void ApplyFilter()
    {
        FilteredWaveformFiles.Clear();
        var term = _searchText.Trim();

        foreach (var file in WaveformFiles)
        {
            if (term.Length == 0 ||
                file.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                FilteredWaveformFiles.Add(file);
            }
        }
    }

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
