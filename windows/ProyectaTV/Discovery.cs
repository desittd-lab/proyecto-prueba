using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ProyectaTV;

public sealed record TvInfo(string Name, string Ip, int Port);

/// <summary>Escucha los anuncios UDP que emite la app del Android TV (puerto 50505).</summary>
public sealed class Discovery : IDisposable
{
    public const int UdpPort = 50505;
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;

    public event Action<TvInfo>? Found;

    public void Start() => _ = Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        try
        {
            _udp = new UdpClient(AddressFamily.InterNetwork) { ExclusiveAddressUse = false };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, UdpPort));

            while (!_cts.IsCancellationRequested)
            {
                var r = await _udp.ReceiveAsync(_cts.Token);
                var text = Encoding.UTF8.GetString(r.Buffer);
                var p = text.Split('|');
                if (p.Length >= 3 && p[0] == "PROYECTATV1" && int.TryParse(p[2], out var port))
                    Found?.Invoke(new TvInfo(p[1], r.RemoteEndPoint.Address.ToString(), port));
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception) { /* sin descubrimiento: el usuario puede escribir la IP */ }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
    }
}
