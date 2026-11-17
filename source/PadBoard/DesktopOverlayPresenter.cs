using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Rectangle = System.Drawing.Rectangle;

namespace ControllerKeyboard;

internal sealed class DesktopOverlayPresenter(FrameworkElement surface) : IDisposable
{
    private const double ResizeMilliseconds = 110;
    private const uint MoveFlags = 0x0015; // NOSIZE | NOZORDER | NOACTIVATE
    private const uint WindowStyle = 0x80000000; // WS_POPUP
    private const uint ExtendedStyle = 0x08080088; // NOACTIVATE | LAYERED | TOOLWINDOW | TOPMOST
    private nint window;
    private nint dc;
    private nint dib;
    private nint previousBitmap;
    private nint pixels;
    private int bufferWidth;
    private int bufferHeight;
    private Rectangle area;
    private string? monitor;
    private double dpi = 1;
    private double fit = 1;
    private double centerX;
    private double centerY;
    private double resizeStart;
    private double resizeFrom = 100;
    private int targetSize = 100;
    private bool dirty = true;
    private Rectangle presented;
    private RenderTargetBitmap? bitmap;
    private SubclassProc? mouseProcedure;

    public event Action<Point>? PanelClicked;

    public nint Handle => window;
    public bool NeedsRedraw => dirty;
    public int MaximumSizePercent => Math.Max(100, (int)(fit / Math.Min(fit, dpi) * 20) * 5);

    public bool Show(nint target, bool retain, int size, double now)
    {
        Forms.Screen screen = target == 0 ? Forms.Screen.FromPoint(Forms.Cursor.Position) : Forms.Screen.FromHandle(target);
        area = screen.WorkingArea;
        bool sameMonitor = monitor == screen.DeviceName;
        if (window == 0)
        {
            window = CreateWindowEx(ExtendedStyle, "Static", "PadBoard", WindowStyle,
                area.Left, area.Top, 1, 1, 0, 0, 0, 0);
            if (window != 0)
            {
                mouseProcedure = HandleMouse;
                if (!SetWindowSubclass(window, mouseProcedure, 1, 0))
                {
                    DestroyWindow(window);
                    window = 0;
                    return false;
                }
            }
        }
        else if (!sameMonitor)
            SetWindowPos(window, 0, area.Left, area.Top, 0, 0, MoveFlags);
        if (window == 0)
            return false;

        monitor = screen.DeviceName;
        uint windowDpi = GetDpiForWindow(window);
        dpi = windowDpi == 0 ? 1 : windowDpi / 96d;
        fit = Math.Min(area.Width / surface.Width, area.Height / surface.Height);
        targetSize = Math.Min(size, MaximumSizePercent);
        resizeFrom = targetSize;
        resizeStart = now;
        if (!retain || !sameMonitor)
        {
            centerX = area.Left + area.Width / 2d;
            double scale = ScaleFor(targetSize);
            centerY = area.Bottom - 12 - surface.Height * scale / 2;
        }

        dirty = true;
        if (!Present(now))
            return false;
        ShowWindow(window, 4); // SW_SHOWNOACTIVATE
        return true;
    }

    public void Hide()
    {
        if (window != 0)
            ShowWindow(window, 0);
    }

    public void Invalidate() => dirty = true;

    private nint HandleMouse(nint handle, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE
            return 3; // MA_NOACTIVATE
        if (message == 0x0084) // WM_NCHITTEST
            return 1; // HTCLIENT
        if (message == 0x0202 && presented.Width > 0 && presented.Height > 0) // WM_LBUTTONUP
        {
            int x = unchecked((short)(lParam.ToInt64() & 0xffff));
            int y = unchecked((short)((lParam.ToInt64() >> 16) & 0xffff));
            PanelClicked?.Invoke(new Point(x * surface.Width / presented.Width, y * surface.Height / presented.Height));
            return 0;
        }
        if (message is >= 0x0201 and <= 0x020E)
            return 0;
        return DefSubclassProc(handle, message, wParam, lParam);
    }

    public void Resize(int size, double now)
    {
        resizeFrom = SizeAt(now);
        resizeStart = now;
        targetSize = size;
    }

    public void Move(double dx, double dy)
    {
        centerX += dx * dpi;
        centerY += dy * dpi;
    }

    private double SizeAt(double now)
    {
        double t = Math.Clamp((now - resizeStart) / ResizeMilliseconds, 0, 1);
        double remaining = 1 - t;
        return targetSize + (resizeFrom - targetSize) * remaining * remaining * remaining;
    }

    private double ScaleFor(double percent) => Math.Min(fit, Math.Min(fit, dpi) * percent / 100);

    public bool Present(double now)
    {
        if (window == 0)
            return false;

        double scale = ScaleFor(SizeAt(now));
        int width = Math.Max(1, (int)Math.Round(surface.Width * scale));
        int height = Math.Max(1, (int)Math.Round(surface.Height * scale));
        centerX = Math.Clamp(centerX, area.Left + width / 2d, area.Right - width / 2d);
        centerY = Math.Clamp(centerY, area.Top + height / 2d, area.Bottom - height / 2d);
        var bounds = new Rectangle((int)Math.Round(centerX - width / 2d), (int)Math.Round(centerY - height / 2d), width, height);

        if (!dirty && bounds.Size == presented.Size)
        {
            if (bounds.Location != presented.Location)
            {
                if (!SetWindowPos(window, 0, bounds.X, bounds.Y, 0, 0, MoveFlags))
                    return false;
                presented = bounds;
            }
            return true;
        }

        if (!EnsureBuffer(width, height))
            return false;

        if (bitmap is null || bitmap.PixelWidth != width || bitmap.PixelHeight != height)
            bitmap = new RenderTargetBitmap(width, height, width * 96 / surface.Width, height * 96 / surface.Height, PixelFormats.Pbgra32);
        else
            bitmap.Clear();
        bitmap.Render(surface);
        int stride = bufferWidth * 4;
        bitmap.CopyPixels(Int32Rect.Empty, pixels, stride * height, stride);

        // DWM receives the finished pixels and their exact destination rectangle together.
        var position = new PointValue { X = bounds.X, Y = bounds.Y };
        var size = new PointValue { X = width, Y = height };
        var source = new PointValue();
        var blend = new Blend { Opacity = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(window, 0, ref position, ref size, dc, ref source, 0, ref blend, 2))
            return false;

        presented = bounds;
        dirty = false;
        return true;
    }

    private bool EnsureBuffer(int width, int height)
    {
        if (dib != 0 && width <= bufferWidth && height <= bufferHeight)
            return true;
        if (dc == 0)
            dc = CreateCompatibleDC(0);
        if (dc == 0)
            return false;

        int newWidth = (width + 127) / 128 * 128;
        int newHeight = (height + 31) / 32 * 32;
        var info = new BitmapHeader
        {
            Size = 40, Width = newWidth, Height = -newHeight, Planes = 1, Bits = 32
        };
        nint replacement = CreateDIBSection(dc, ref info, 0, out nint newPixels, 0, 0);
        if (replacement == 0)
            return false;

        nint previous = SelectObject(dc, replacement);
        if (previous == 0 || previous == -1)
        {
            DeleteObject(replacement);
            return false;
        }
        if (dib != 0)
            DeleteObject(dib);
        else
            previousBitmap = previous;
        dib = replacement;
        pixels = newPixels;
        bufferWidth = newWidth;
        bufferHeight = newHeight;
        return true;
    }

    public void Dispose()
    {
        if (window != 0)
        {
            if (mouseProcedure is not null)
                RemoveWindowSubclass(window, mouseProcedure, 1);
            DestroyWindow(window);
        }
        if (dib != 0)
        {
            SelectObject(dc, previousBitmap);
            DeleteObject(dib);
        }
        if (dc != 0)
            DeleteDC(dc);
        window = dib = dc = 0;
        bitmap = null;
        mouseProcedure = null;
    }

    private delegate nint SubclassProc(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointValue { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Blend { public byte Operation, Flags, Opacity, AlphaFormat; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, Bits;
        public uint Compression, ImageSize;
        public int XResolution, YResolution;
        public uint ColorsUsed, ImportantColors;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowEx(uint extendedStyle,
        string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint args);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(nint window, nint destinationDc, ref PointValue position,
        ref PointValue size, nint sourceDc, ref PointValue source, uint colorKey, ref Blend blend, uint flags);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapHeader info, uint usage, out nint data, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
