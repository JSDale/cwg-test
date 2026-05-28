using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

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

    // Matches every quoted entry in an MMEMory:CATalog? response: "name,TYPE,size"
    private static readonly Regex CatalogEntryRegex = new("\"([^\"]+)\"", RegexOptions.Compiled);

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

    public async Task<IReadOnlyList<string>> GetWaveformFilesAsync(
        string rootPath = "/var/user", CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        var results = new List<string>();
        await _lock.WaitAsync(ct);
        try
        {
            await WalkDirectoryAsync(rootPath, results, depth: 0, ct);
        }
        finally
        {
            _lock.Release();
        }
        return results;
    }

    private async Task WalkDirectoryAsync(
        string path, List<string> waveforms, int depth, CancellationToken ct)
    {
        if (depth > 10) return; // guard against unexpected deep trees

        await SendCommandAsync($":MMEMory:CATalog? \"{path}\"", ct);
        var response = await ReadResponseAsync(ct);

        foreach (Match m in CatalogEntryRegex.Matches(response))
        {
            // Each entry inside quotes is: "<name>,<TYPE>,<size>"
            // Split from the right so names containing commas are handled safely.
            var parts = m.Groups[1].Value.Split(',');
            if (parts.Length < 3) continue;

            var name = string.Join(",", parts[..^2]);
            var type = parts[^2].Trim();

            if (name is "." or "..") continue;

            if (type.Equals("DIR", StringComparison.OrdinalIgnoreCase))
            {
                await WalkDirectoryAsync($"{path}/{name}", waveforms, depth + 1, ct);
            }
            else if (name.EndsWith(".wv", StringComparison.OrdinalIgnoreCase))
            {
                waveforms.Add($"{path}/{name}");
            }
        }
    }

    public async Task SelectWaveformAsync(string instrumentPath, CancellationToken ct = default)
    {
        if (!IsConnected)
            throw new InvalidOperationException("Not connected.");

        // Strip .wv extension — the instrument accepts the path without it
        var pathWithoutExt = instrumentPath.EndsWith(".wv", StringComparison.OrdinalIgnoreCase)
            ? instrumentPath[..^3]
            : instrumentPath;

        await _lock.WaitAsync(ct);
        try
        {
            await SendCommandAsync($":SOURce1:BB:ARBitrary:WAVeform:SELect \"{pathWithoutExt}\"", ct);
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
        var buffer = new byte[65536];
        var sb = new StringBuilder();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(15_000);

        while (true)
        {
            int bytesRead = await _stream!.ReadAsync(buffer, cts.Token);
            sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
            if (sb.ToString().Contains('\n'))
                break;
        }

        return sb.ToString().TrimEnd('\n', '\r');
    }

    public void Dispose()
    {
        Disconnect();
        _lock.Dispose();
    }
}
