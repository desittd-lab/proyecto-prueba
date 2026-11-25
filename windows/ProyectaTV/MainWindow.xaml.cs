using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProyectaTV;

public class TvItem
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; } = 50506;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public sealed record Snap(ViewMode Mode, bool FullZoom, double Zoom, bool Cursor, int Fps, int MaxEdge, int Quality);

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TvItem> _tvs = new();
    private readonly Discovery _discovery = new();
    private readonly DispatcherTimer _prune = new() { Interval = TimeSpan.FromSeconds(3) };
    private TvConnection? _conn;
    private CancellationTokenSource? _proj;
    private volatile Snap _snap = new(ViewMode.Horizontal, true, 2.0, true, 24, 1600, 68);
    private bool _ready;
    private bool _suppressRot;

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProyectaTV");
    private static readonly string LastIpFile = Path.Combine(SettingsDir, "last-ip.txt");

    public MainWindow()
    {
        InitializeComponent();
        LstTv.ItemsSource = _tvs;

        try
        {
            if (File.Exists(LastIpFile)) TxtIp.Text = File.ReadAllText(LastIpFile).Trim();
        }
        catch { }

        _ready = true;
        ApplyUiState();
        _snap = BuildSnap();

        _discovery.Found += t => Dispatcher.InvokeAsync(() => OnTvFound(t));
        _discovery.Start();

        _prune.Tick += (_, _) =>
        {
            var limit = DateTime.UtcNow.AddSeconds(-10);
            foreach (var old in _tvs.Where(t => t.LastSeen < limit).ToList()) _tvs.Remove(old);
        };
        _prune.Start();
    }

    // ---------- Ventana ----------
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        StopProjection();
        _prune.Stop();
        _discovery.Dispose();
        _conn?.Dispose();
        base.OnClosed(e);
    }

    // ---------- Descubrimiento ----------
    private void OnTvFound(TvInfo t)
    {
        var existing = _tvs.FirstOrDefault(x => x.Ip == t.Ip);
        if (existing == null)
        {
            _tvs.Add(new TvItem { Name = t.Name, Ip = t.Ip, Port = t.Port, LastSeen = DateTime.UtcNow });
            if (LstTv.SelectedItem == null && string.IsNullOrWhiteSpace(TxtIp.Text)) LstTv.SelectedIndex = 0;
        }
        else
        {
            existing.LastSeen = DateTime.UtcNow;
        }
    }

    private void LstTv_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstTv.SelectedItem is TvItem item) TxtIp.Text = item.Ip;
    }

    // ---------- Conexión ----------
    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_conn != null)
        {
            DisconnectNow();
            return;
        }

        var ipText = TxtIp.Text.Trim();
        if (!IPAddress.TryParse(ipText, out var ip))
        {
            SetStatus("Escribe una IP válida, por ejemplo 192.168.1.50");
            return;
        }

        int port = _tvs.FirstOrDefault(t => t.Ip == ipText)?.Port ?? 50506;
        BtnConnect.IsEnabled = false;
        SetStatus("Conectando…");

        var c = new TvConnection();
        c.InfoChanged += () => Dispatcher.InvokeAsync(() => OnTvInfo(c));
        c.Disconnected += reason => Dispatcher.InvokeAsync(() => OnDisconnected(c, reason));

        try
        {
            await c.ConnectAsync(ip, port);
            _conn = c;
            try
            {
                Directory.CreateDirectory(SettingsDir);
                File.WriteAllText(LastIpFile, ipText);
            }
            catch { }
            BtnConnect.Content = "Desconectar";
            BtnStart.IsEnabled = true;
            SetStatus($"Conectado a {c.TvName}");
        }
        catch (Exception ex)
        {
            c.Dispose();
            SetStatus("No se pudo conectar: " + ex.Message);
        }
        finally
        {
            BtnConnect.IsEnabled = true;
        }
    }

    private void DisconnectNow()
    {
        StopProjection();
        _conn?.Dispose();
        _conn = null;
        ResetConnectionUi("Sin conexión");
    }

    private void OnDisconnected(TvConnection c, string reason)
    {
        if (_conn != c) return;
        StopProjection();
        _conn?.Dispose();
        _conn = null;
        ResetConnectionUi("Conexión perdida: " + reason);
    }

    private void ResetConnectionUi(string status)
    {
        BtnConnect.Content = "Conectar";
        BtnStart.IsEnabled = false;
        TxtStats.Text = "Sin proyección";
        SetStatus(status);
    }

    private void OnTvInfo(TvConnection c)
    {
        if (_conn != c && _conn != null) return;
        _suppressRot = true;
        switch (c.TvRot)
        {
            case 0: RbRot0.IsChecked = true; break;
            case 1: RbRot1.IsChecked = true; break;
            case 2: RbRot2.IsChecked = true; break;
            default: RbRot3.IsChecked = true; break;
        }
        _suppressRot = false;
        SetStatus($"Conectado a {c.TvName} · pantalla {c.TvWidth}×{c.TvHeight}");
    }

    private void SetStatus(string text) => TxtStatus.Text = text;

    // ---------- Opciones ----------
    private void Orient_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyUiState();
        _snap = BuildSnap();
    }

    private void Opt_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ApplyUiState();
        _snap = BuildSnap();
    }

    private void Zoom_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ready) return;
        TxtZoom.Text = $"Zoom: {SldZoom.Value:0.0}×";
        _snap = BuildSnap();
    }

    private void Rot_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready || _suppressRot) return;
        if (sender is RadioButton rb && int.TryParse(rb.Tag as string, out var q))
            _conn?.SendRotation(q);
    }

    private void ApplyUiState()
    {
        bool vertical = RbVert.IsChecked == true;
        PnlVertical.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
        PnlZoom.Visibility = ChkFull.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        TxtZoom.Text = $"Zoom: {SldZoom.Value:0.0}×";
    }

    private Snap BuildSnap()
    {
        int fps = RbF15.IsChecked == true ? 15 : RbF30.IsChecked == true ? 30 : 24;
        int edge, quality;
        if (RbQ1.IsChecked == true) { edge = 1280; quality = 55; }
        else if (RbQ3.IsChecked == true) { edge = 1920; quality = 82; }
        else { edge = 1600; quality = 68; }

        return new Snap(
            RbVert.IsChecked == true ? ViewMode.Vertical : ViewMode.Horizontal,
            ChkFull.IsChecked == true,
            SldZoom.Value,
            ChkCursor.IsChecked == true,
            fps, edge, quality);
    }

    // ---------- Proyección ----------
    private void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        if (_proj != null)
        {
            StopProjection();
            return;
        }
        if (_conn == null) return;

        _proj = new CancellationTokenSource();
        var conn = _conn;
        var ct = _proj.Token;

        BtnStart.Content = "■  Detener proyección";
        BtnStart.Background = (Brush)FindResource("StopBrush");
        TxtStats.Text = "Iniciando…";

        Task.Run(() => ProjectLoopAsync(conn, ct));
    }

    private void StopProjection()
    {
        if (_proj == null) return;
        _proj.Cancel();
        _proj = null;
        BtnStart.Content = "▶  Proyectar escritorio";
        BtnStart.Background = (Brush)FindResource("NeonBrush");
        TxtStats.Text = "Proyección detenida";
    }

    private static (int w, int h) ComputeOut(int tw, int th, int maxEdge)
    {
        if (tw <= 0 || th <= 0) { tw = 1080; th = 1920; }
        double sc = Math.Min(1.0, (double)maxEdge / Math.Max(tw, th));
        int w = (int)(tw * sc) & ~1;
        int h = (int)(th * sc) & ~1;
        return (Math.Max(w, 64), Math.Max(h, 64));
    }

    private async Task ProjectLoopAsync(TvConnection conn, CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var cap = new ScreenCapturer();
            using var ms = new MemoryStream(1 << 20);
            var clock = Stopwatch.StartNew();
            long next = 0;
            int frames = 0;
            long bytes = 0;
            long lastStat = 0;

            while (!ct.IsCancellationRequested)
            {
                var s = _snap;

                // Espera a que el TV confirme cuadros anteriores (evita acumular retraso).
                if (!await conn.Credits.WaitAsync(2000, ct)) continue;

                var (w, h) = ComputeOut(conn.TvWidth, conn.TvHeight, s.MaxEdge);
                var opts = new CaptureOptions
                {
                    Mode = s.Mode,
                    FullZoom = s.FullZoom,
                    Zoom = s.Zoom,
                    ShowCursor = s.Cursor,
                    OutW = w,
                    OutH = h
                };

                var frame = cap.Frame(opts);
                ms.SetLength(0);
                ScreenCapturer.EncodeJpeg(frame, s.Quality, ms);
                int len = (int)ms.Length;

                if (!conn.SendFrame(ms.GetBuffer(), len))
                {
                    error = "Se perdió la conexión con el TV";
                    break;
                }

                frames++;
                bytes += len;

                long now = clock.ElapsedMilliseconds;
                if (now - lastStat >= 1000)
                {
                    double secs = (now - lastStat) / 1000.0;
                    string text = $"{frames / secs:0} FPS · {bytes * 8 / secs / 1_000_000:0.0} Mbps · {w}×{h}";
                    Dispatcher.InvokeAsync(() => { if (_proj != null) TxtStats.Text = text; });
                    frames = 0;
                    bytes = 0;
                    lastStat = now;
                }

                next += 1000 / s.Fps;
                long wait = next - clock.ElapsedMilliseconds;
                if (wait > 0) await Task.Delay((int)wait, ct);
                else next = clock.ElapsedMilliseconds;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { error = ex.Message; }

        Dispatcher.InvokeAsync(() =>
        {
            if (_proj != null && _proj.Token == ct)
            {
                StopProjection();
                if (error != null) TxtStats.Text = "Error: " + error;
            }
        });
    }
}
