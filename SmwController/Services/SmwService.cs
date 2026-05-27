using System.IO;
using System.Net.Sockets;
using System.Text;

namespace SmwController.Services;

/// <summary>
/// Communicates with the R&amp;S SMW200A via raw SCPI socket (IEEE 488.2 / VISA TCP).
/// The instrument listens on port 5025 by default.
/// </summary>
public sealed class SmwService : ISmwService
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _client?.Connected == true;

    public async Task ConnectAsync(string ipAddress, int port, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            Disconnect();

            _client = new TcpClient();
            _client.ReceiveTimeout = 10_000;
            _client.SendTimeout = 10_000;
            await _client.ConnectAsync(ipAddress, port, ct);
            _stream = _client.GetStream();

            // Verify identity
            await SendCommandAsync("*IDN?", ct);
            var idn = await ReadResponseAsync(ct);
            if (!idn.Contains("SMW200A", StringComparison.OrdinalIgnoreCase) &&
                !idn.Contains("SMW", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unexpected instrument response: {idn}");
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Disconnect()
    {
        _stream?.Close();
        _client?.Close();
        _stream = null;
        _client = null;
    }

    public async Task LoadWaveformAsync(string localFilePath, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        var fileName = Path.GetFileName(localFilePath);
        var instrumentPath = $"/var/user/{fileName}";
        var fileBytes = await File.ReadAllBytesAsync(localFilePath, ct);

        await _lock.WaitAsync(ct);
        try
        {
            // Upload file using MMEMory:DATA with IEEE 488.2 arbitrary block header
            var header = BuildBlockHeader(fileBytes.Length);
            var pathBytes = Encoding.ASCII.GetBytes($":MMEMory:DATA \"{instrumentPath}\",");
            var terminator = new byte[] { (byte)'\n' };

            var combined = new byte[pathBytes.Length + header.Length + fileBytes.Length + terminator.Length];
            Buffer.BlockCopy(pathBytes, 0, combined, 0, pathBytes.Length);
            Buffer.BlockCopy(header, 0, combined, pathBytes.Length, header.Length);
            Buffer.BlockCopy(fileBytes, 0, combined, pathBytes.Length + header.Length, fileBytes.Length);
            Buffer.BlockCopy(terminator, 0, combined, combined.Length - 1, 1);

            await _stream!.WriteAsync(combined, ct);
            await _stream.FlushAsync(ct);

            // Wait for upload to complete
            await Task.Delay(500, ct);

            // Select the waveform in the ARB (omit extension as per manual)
            var nameWithoutExt = Path.GetFileNameWithoutExtension(instrumentPath);
            var dir = Path.GetDirectoryName(instrumentPath)!.Replace('\\', '/');
            var arbPath = $"{dir}/{nameWithoutExt}";

            await SendCommandAsync($":SOURce1:BB:ARBitrary:WAVeform:SELect \"{arbPath}\"", ct);
            await SendCommandAsync(":SOURce1:BB:ARBitrary:STATe ON", ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetPowerLevelAsync(double dBm, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        await _lock.WaitAsync(ct);
        try
        {
            await SendCommandAsync($":SOURce1:POWer:LEVel:IMMediate:AMPLitude {dBm:F1}", ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StartRfOutputAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        await _lock.WaitAsync(ct);
        try
        {
            await SendCommandAsync(":OUTPut1:STATe ON", ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopRfOutputAsync(CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        await _lock.WaitAsync(ct);
        try
        {
            await SendCommandAsync(":OUTPut1:STATe OFF", ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string> QueryAsync(string query, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        await _lock.WaitAsync(ct);
        try
        {
            await SendCommandAsync(query, ct);
            return await ReadResponseAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SendCommandAsync(string command, CancellationToken ct)
    {
        var bytes = Encoding.ASCII.GetBytes(command + "\n");
        await _stream!.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    private async Task<string> ReadResponseAsync(CancellationToken ct)
    {
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(5_000);

        while (true)
        {
            int bytesRead = await _stream!.ReadAsync(buffer, cts.Token);
            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            if (sb.ToString().Contains('\n'))
                break;
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Builds an IEEE 488.2 arbitrary block header: #&lt;digits&gt;&lt;length&gt;
    /// </summary>
    private static byte[] BuildBlockHeader(int byteCount)
    {
        var lengthStr = byteCount.ToString();
        var header = $"#{lengthStr.Length}{lengthStr}";
        return Encoding.ASCII.GetBytes(header);
    }

    public void Dispose()
    {
        Disconnect();
        _lock.Dispose();
    }
}
