using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProyectaTV;

/// <summary>
/// Protocolo TCP: [tipo:1][largo:4 big-endian][datos].
/// 1 HELLO (TV→PC) "nombre|ancho|alto|giro"   2 FRAME (PC→TV) JPEG
/// 3 ROT (PC→TV) 1 byte (0..3)                4 ACK (TV→PC) cuadro mostrado
/// </summary>
public sealed class TvConnection : IDisposable
{
    public const int T_HELLO = 1, T_FRAME = 2, T_ROT = 3, T_ACK = 4;

    private Socket? _sock;
    private readonly object _writeLock = new();
    private byte[] _pkt = new byte[1 << 20];
    private readonly CancellationTokenSource _cts = new();
    private int _closed;

    public int TvWidth { get; private set; } = 1080;
    public int TvHeight { get; private set; } = 1920;
    public int TvRot { get; private set; } = 1;
    public string TvName { get; private set; } = "Android TV";

    /// <summary>Control de flujo: máximo 2 cuadros sin confirmar para evitar retraso.</summary>
    public SemaphoreSlim Credits { get; } = new(2, 2);

    public event Action? InfoChanged;
    public event Action<string>? Disconnected;

    public async Task ConnectAsync(IPAddress ip, int port)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            NoDelay = true,
            SendBufferSize = 1 << 20
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await s.ConnectAsync(new IPEndPoint(ip, port), timeout.Token);
        }
        catch (OperationCanceledException)
        {
            s.Dispose();
            throw new TimeoutException("Tiempo agotado. ¿Está abierta la app en el TV y en la misma red Wi-Fi?");
        }
        catch
        {
            s.Dispose();
            throw;
        }
        _sock = s;
        _ = Task.Run(ReadLoopAsync);
    }

    private async Task ReadLoopAsync()
    {
        var s = _sock!;
        string reason = "Conexión cerrada";
        try
        {
            using var ns = new NetworkStream(s, ownsSocket: false);
            var hdr = new byte[5];
            var buf = new byte[4096];
            while (!_cts.IsCancellationRequested)
            {
                await ns.ReadExactlyAsync(hdr.AsMemory(), _cts.Token);
                int len = BinaryPrimitives.ReadInt32BigEndian(hdr.AsSpan(1, 4));
                if (len < 0 || len > 1_000_000) throw new IOException("Mensaje inválido");
                if (len > buf.Length) buf = new byte[len];
                if (len > 0) await ns.ReadExactlyAsync(buf.AsMemory(0, len), _cts.Token);

                switch (hdr[0])
                {
                    case T_ACK:
                        if (Credits.CurrentCount < 2) Credits.Release();
                        break;
                    case T_HELLO:
                        ParseHello(Encoding.UTF8.GetString(buf, 0, len));
                        InfoChanged?.Invoke();
                        break;
                }
            }
            return;
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { reason = ex.Message; }
        RaiseDisconnected(reason);
    }

    private void ParseHello(string text)
    {
        var p = text.Split('|');
        if (p.Length >= 1 && p[0].Length > 0) TvName = p[0];
        if (p.Length >= 3 && int.TryParse(p[1], out var w) && int.TryParse(p[2], out var h) && w > 0 && h > 0)
        {
            TvWidth = w; TvHeight = h;
        }
        if (p.Length >= 4 && int.TryParse(p[3], out var r)) TvRot = ((r % 4) + 4) % 4;
    }

    public bool SendFrame(byte[] data, int len) => Send(T_FRAME, data, len);

    public void SendRotation(int quarterTurns) => Send(T_ROT, new[] { (byte)quarterTurns }, 1);

    private bool Send(int type, byte[] data, int len)
    {
        var s = _sock;
        if (s == null || _closed != 0) return false;
        try
        {
            lock (_writeLock)
            {
                int total = len + 5;
                if (_pkt.Length < total) _pkt = new byte[total + (total >> 1)];
                _pkt[0] = (byte)type;
                BinaryPrimitives.WriteInt32BigEndian(_pkt.AsSpan(1, 4), len);
                Buffer.BlockCopy(data, 0, _pkt, 5, len);
                int off = 0;
                while (off < total) off += s.Send(_pkt, off, total - off, SocketFlags.None);
            }
            return true;
        }
        catch (Exception ex)
        {
            RaiseDisconnected(ex.Message);
            return false;
        }
    }

    private void RaiseDisconnected(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        try { _sock?.Close(); } catch { }
        Disconnected?.Invoke(reason);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _closed, 1);
        _cts.Cancel();
        try { _sock?.Close(); } catch { }
    }
}
