using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ProyectaTV;

public enum ViewMode { Horizontal, Vertical }

public sealed class CaptureOptions
{
    public ViewMode Mode;
    public bool FullZoom;
    public double Zoom = 2.0;
    public bool ShowCursor = true;
    public int OutW = 1080;
    public int OutH = 1920;
}

/// <summary>
/// Captura el escritorio principal y compone el cuadro final con el tamaño exacto
/// que pide el TV. En modo vertical + zoom solo captura la región alrededor del mouse.
/// </summary>
public sealed class ScreenCapturer : IDisposable
{
    private Bitmap? _cap;
    private Bitmap? _out;
    private double _cx, _cy;
    private bool _init;

    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    public Bitmap Frame(CaptureOptions o)
    {
        int sw = Native.GetSystemMetrics(0);
        int sh = Native.GetSystemMetrics(1);
        Native.GetCursorPos(out var pt);

        bool zoom = o.Mode == ViewMode.Vertical && o.FullZoom;
        Rectangle src;

        if (zoom)
        {
            double aspect = (double)o.OutW / o.OutH;
            double rh = sh;
            double rw = rh * aspect;
            if (rw > sw) { rw = sw; rh = rw / aspect; }
            double z = Math.Max(1.0, o.Zoom);
            rw /= z; rh /= z;
            int iw = Math.Max(32, (int)Math.Round(rw));
            int ih = Math.Max(32, (int)Math.Round(rh));

            if (!_init) { _cx = pt.X; _cy = pt.Y; _init = true; }
            else { _cx += (pt.X - _cx) * 0.22; _cy += (pt.Y - _cy) * 0.22; }

            int x = (int)Math.Round(_cx - iw / 2.0);
            int y = (int)Math.Round(_cy - ih / 2.0);
            x = Math.Clamp(x, 0, Math.Max(0, sw - iw));
            y = Math.Clamp(y, 0, Math.Max(0, sh - ih));
            src = new Rectangle(x, y, Math.Min(iw, sw), Math.Min(ih, sh));
        }
        else
        {
            _init = false;
            src = new Rectangle(0, 0, sw, sh);
        }

        if (_cap == null || _cap.Width != src.Width || _cap.Height != src.Height)
        {
            _cap?.Dispose();
            _cap = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppRgb);
        }
        if (_out == null || _out.Width != o.OutW || _out.Height != o.OutH)
        {
            _out?.Dispose();
            _out = new Bitmap(o.OutW, o.OutH, PixelFormat.Format24bppRgb);
        }

        using (var g = Graphics.FromImage(_cap))
        {
            g.CopyFromScreen(src.X, src.Y, 0, 0, src.Size,
                CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);
            if (o.ShowCursor) DrawCursor(g, src);
        }

        using (var g = Graphics.FromImage(_out))
        {
            g.Clear(System.Drawing.Color.Black);
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            Rectangle dst;
            if (zoom)
            {
                dst = new Rectangle(0, 0, o.OutW, o.OutH);
            }
            else
            {
                double scale = Math.Min((double)o.OutW / sw, (double)o.OutH / sh);
                int dw = (int)Math.Round(sw * scale);
                int dh = (int)Math.Round(sh * scale);
                dst = new Rectangle((o.OutW - dw) / 2, (o.OutH - dh) / 2, dw, dh);
            }
            g.DrawImage(_cap, dst, 0, 0, _cap.Width, _cap.Height, GraphicsUnit.Pixel);
        }

        return _out;
    }

    private static void DrawCursor(Graphics g, Rectangle src)
    {
        var ci = new Native.CURSORINFO { cbSize = Marshal.SizeOf<Native.CURSORINFO>() };
        if (!Native.GetCursorInfo(ref ci) || (ci.flags & Native.CURSOR_SHOWING) == 0) return;

        int hx = 0, hy = 0;
        if (Native.GetIconInfo(ci.hCursor, out var ii))
        {
            hx = ii.xHotspot; hy = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) Native.DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero) Native.DeleteObject(ii.hbmColor);
        }

        IntPtr hdc = g.GetHdc();
        try
        {
            Native.DrawIconEx(hdc, ci.ptScreenPos.X - hx - src.X, ci.ptScreenPos.Y - hy - src.Y,
                ci.hCursor, 0, 0, 0, IntPtr.Zero, Native.DI_NORMAL);
        }
        finally { g.ReleaseHdc(hdc); }
    }

    public static void EncodeJpeg(Bitmap bmp, int quality, Stream target)
    {
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
        bmp.Save(target, JpegCodec, ep);
    }

    public void Dispose()
    {
        _cap?.Dispose();
        _out?.Dispose();
    }
}
