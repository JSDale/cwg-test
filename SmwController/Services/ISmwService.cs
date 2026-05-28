namespace SmwController.Services;

public interface ISmwService : IDisposable
{
    bool IsConnected { get; }

    Task ConnectAsync(string ipAddress, int port, CancellationToken ct = default);
    void Disconnect();

    /// <summary>
    /// Recursively walks <paramref name="rootPath"/> on the instrument and returns
    /// the full instrument path of every *.wv file found.
    /// </summary>
    Task<IReadOnlyList<string>> GetWaveformFilesAsync(string rootPath = "/var/user", CancellationToken ct = default);

    /// <summary>
    /// Selects an existing waveform file already on the instrument and enables the ARB generator.
    /// </summary>
    Task SelectWaveformAsync(string instrumentPath, CancellationToken ct = default);

    Task SetPowerLevelAsync(double dBm, CancellationToken ct = default);
    Task StartRfOutputAsync(CancellationToken ct = default);
    Task StopRfOutputAsync(CancellationToken ct = default);

    Task<string> QueryAsync(string query, CancellationToken ct = default);
}
