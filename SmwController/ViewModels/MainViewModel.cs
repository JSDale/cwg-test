using Microsoft.Win32;
using SmwController.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmwController.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISmwService _smwService;

    private string _ipAddress = "192.168.1.1";
    private int _port = 5025;
    private string _waveformPath = string.Empty;
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

        ConnectCommand      = new AsyncRelayCommand(ConnectAsync,    () => !IsBusy);
        DisconnectCommand   = new RelayCommand(Disconnect,           () => _smwService.IsConnected && !IsBusy);
        BrowseWaveformCommand = new RelayCommand(BrowseWaveform);
        LoadWaveformCommand = new AsyncRelayCommand(LoadWaveformAsync,
            () => _smwService.IsConnected && !string.IsNullOrWhiteSpace(WaveformPath) && !IsBusy && !IsRfActive);
        StartRfCommand = new AsyncRelayCommand(StartRfAsync,
            () => _smwService.IsConnected && !IsRfActive && !IsBusy);
        StopRfCommand = new RelayCommand(StopRf, () => IsRfActive);
    }

    // ── Bindable properties ──────────────────────────────────────────────────

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

    public string WaveformPath
    {
        get => _waveformPath;
        set => SetProperty(ref _waveformPath, value);
    }

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

    /// <summary>True only during short blocking operations (connect, load waveform).</summary>
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

    public bool IsConnected => _smwService.IsConnected;

    // ── Commands ─────────────────────────────────────────────────────────────

    public AsyncRelayCommand ConnectCommand { get; }
    public RelayCommand DisconnectCommand { get; }
    public RelayCommand BrowseWaveformCommand { get; }
    public AsyncRelayCommand LoadWaveformCommand { get; }
    public AsyncRelayCommand StartRfCommand { get; }
    public RelayCommand StopRfCommand { get; }

    // ── Command implementations ───────────────────────────────────────────────

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

    private void BrowseWaveform()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select Waveform File",
            Filter = "Waveform files (*.wv)|*.wv|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            WaveformPath = dlg.FileName;
    }

    private async Task LoadWaveformAsync(CancellationToken ct)
    {
        IsBusy = true;
        Status = "Uploading waveform to instrument…";
        try
        {
            await _smwService.LoadWaveformAsync(WaveformPath, ct);
            Status = "Waveform loaded. ARB generator enabled.";
        }
        catch (Exception ex)
        {
            Status = $"Waveform load failed: {ex.Message}";
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
            // Set initial level and enable RF output
            await _smwService.SetPowerLevelAsync(StartLevel, token);
            await _smwService.StartRfOutputAsync(token);

            // Ramp from StartLevel to StopLevel in StepSize increments,
            // dwelling DwellSeconds at each level.
            double level = StartLevel;
            while (!token.IsCancellationRequested)
            {
                await _smwService.SetPowerLevelAsync(level, token);
                CurrentLevel = level;
                Status = $"RF ON — Level: {level:+0.0;-0.0} dBm";

                if (level >= StopLevel)
                {
                    // Final level reached — hold until user presses Stop
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
            // Always disable RF output when done or stopped
            try { await _smwService.StopRfOutputAsync(CancellationToken.None); }
            catch { /* best-effort */ }

            IsRfActive = false;
            _rampCts?.Dispose();
            _rampCts = null;
        }
    }

    private void StopRf() => _rampCts?.Cancel();

    // ── INotifyPropertyChanged ─────────────────────────────────────────────

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
