namespace SmwController.Services;

public interface ISmwService : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string ipAddress, int port, CancellationToken ct = default);
    void Disconnect();

    /// <summary>
    /// Uploads a waveform file from the local PC to /var/user/ on the instrument,
    /// selects it in the ARB generator, and enables the ARB generator.
    /// </summary>
    Task LoadWaveformAsync(string localFilePath, CancellationToken ct = default);

    Task SetPowerLevelAsync(double dBm, CancellationToken ct = default);
    Task StartRfOutputAsync(CancellationToken ct = default);
    Task StopRfOutputAsync(CancellationToken ct = default);

    Task<string> QueryAsync(string query, CancellationToken ct = default);
}
