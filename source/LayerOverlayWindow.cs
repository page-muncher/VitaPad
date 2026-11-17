using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VitaPad;

public sealed record OverlayEntry(string Id, string Title, string Value, bool LeftSide);

/// <summary>A passive, click-through controller reference. Its owner decides when to display it.</summary>
public sealed class LayerOverlayWindow : Window
{
    static readonly SolidColorBrush BackgroundBrush = Paint("#171A1D");
    static readonly SolidColorBrush CardBrush = Paint("#252A2F");
    static readonly SolidColorBrush ForegroundBrush = Paint("#F3F2EF");
    static readonly SolidColorBrush MutedBrush = Paint("#A9B0B6");
    static readonly SolidColorBrush AccentBrush = Paint("#F26972");
    static readonly SolidColorBrush StrokeBrush = Paint("#48515A");
    static readonly SolidColorBrush PressedBrush = Paint("#493038");
    const int EdgeInset = 14;
    const double PreviewScale = 1.4;
    readonly Border root;
    readonly Dictionary<string, Border> cards = [];
    string signature = "";
    nint handle;
    bool closed;
    System.Drawing.Rectangle? anchorArea;
    double anchorScale = 1;

    public LayerOverlayWindow()
    {
        Title = "VitaPad · Layer reference";
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Opacity = .75;
        Background = Brushes.Transparent;
        Focusable = false;
        IsHitTestVisible = false;
        root = new Border
        {
            Background = BackgroundBrush,
            BorderBrush = StrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(11),
            Padding = new Thickness(10, 8, 10, 8),
            LayoutTransform = new ScaleTransform(PreviewScale, PreviewScale)
        };
        Content = root;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(handle, -20).ToInt64();
            // NOACTIVATE + TRANSPARENT + TOOLWINDOW: pointer and keyboard remain with the user's app.
            SetWindowLongPtr(handle, -20, new nint(style | 0x08000000L | 0x20L | 0x80L));
            HwndSource.FromHwnd(handle)?.AddHook((nint hwnd, int msg, nint wp, nint lp, ref bool handled) =>
            {
                if (msg == 0x21) { handled = true; return new nint(3); } // MA_NOACTIVATE
                if (msg == 0x84) { handled = true; return new nint(-1); } // HTTRANSPARENT
                if (msg == 0x46) AnchorWindowPosition(hwnd, lp); // WM_WINDOWPOSCHANGING
                return nint.Zero;
            });
        };
        Closed += (_, _) => closed = true;
    }

    public static string ControlName(string id) => id switch
    {
        "A" => "× Cross",
        "B" => "○ Circle",
        "X" => "□ Square",
        "Y" => "△ Triangle",
        _ => id
    };

    public static string FormatBinding(Binding binding)
    {
        if (binding.PadBoard) return binding.Label;
        string Key(int code) => Engine.KeyName(code)
            .Replace("Left ", "L ").Replace("Right ", "R ").Replace("Print Screen", "PrtSc")
            .Replace("Scroll Lock", "ScrLk").Replace("Num Lock", "NumLk").Replace("Backspace", "Bksp")
            .Replace("Numpad ", "Num ");
        return string.Join(" + ", binding.Keys.Select(Key).Concat(binding.Mouse));
    }

    /// <summary>Only effective, assigned controls; inactive stick key mappings are deliberately excluded.</summary>
    public static IReadOnlyList<OverlayEntry> GetEntries(Profile profile, int layerIndex, bool bothBumpers)
    {
        var layer = profile.Layers[Math.Clamp(layerIndex, 0, 2)];
        List<OverlayEntry> entries = [];
        string[] left = ["L2", "Select", "D-pad Up", "D-pad Left", "D-pad Right", "D-pad Down", "Left stick Up", "Left stick Left", "Left stick Right", "Left stick Down", "L3"];
        string[] right = ["R2", "Start", "Y", "X", "B", "A", "Right stick Up", "Right stick Left", "Right stick Right", "Right stick Down", "R3"];
        void Add(string id, bool leftSide)
        {
            if (id == "R3" && bothBumpers) return;
            if (id.StartsWith("Left stick") && layer.LeftStick != "Keys") return;
            if (id.StartsWith("Right stick") && layer.RightStick != "Keys") return;
            if (layer.Bindings.TryGetValue(id, out var binding) && binding.Label.Length > 0)
                entries.Add(new(id, ControlName(id), FormatBinding(binding), leftSide));
        }
        foreach (var id in left) Add(id, true);
        foreach (var id in right) Add(id, false);
        if (layer.LeftStick != "Keys") entries.Add(new("Left stick", "Left stick", layer.LeftStick == "Pointer" ? "Mouse pointer" : "Mouse scroll", true));
        if (layer.RightStick != "Keys") entries.Add(new("Right stick", "Right stick", layer.RightStick == "Pointer" ? "Mouse pointer" : "Mouse scroll", false));
        return entries;
    }

    public void ShowLayer(Profile profile, int layerIndex, bool bothBumpers, IReadOnlySet<string> active)
    {
        if (closed) return;
        bool opening = !IsVisible;
        if (opening || anchorArea is null)
        {
            // Keep a single monitor throughout this visible session, including modifier changes.
            var area = System.Windows.Forms.Screen.FromHandle(GetForegroundWindow()).WorkingArea;
            anchorArea = area;
            anchorScale = GetMonitorScale(area.Left, area.Top);
        }
        var work = anchorArea.Value;
        double maxWidth = Math.Max(220, (work.Width - 2 * EdgeInset) / anchorScale);
        double maxHeight = Math.Max(180, (work.Height - 2 * EdgeInset) / anchorScale);
        var entries = GetEntries(profile, layerIndex, bothBumpers);
        var text = new StringBuilder().Append(profile.Name).Append('|').Append(layerIndex).Append('|').Append(bothBumpers)
            .Append('|').Append(maxWidth).Append('|').Append(maxHeight);
        foreach (var item in entries) text.Append('|').Append(item.Id).Append(':').Append(item.Value);
        string next = text.ToString();
        if (next != signature)
        {
            signature = next;
            Build(profile.Name, layerIndex, bothBumpers, entries, maxWidth, maxHeight);
        }
        foreach (var (id, card) in cards)
        {
            bool pressed = active.Contains(id) || (id is "Left stick" or "Right stick" && active.Any(a => a.StartsWith(id + " ")));
            card.BorderBrush = pressed ? AccentBrush : StrokeBrush;
            card.Background = pressed ? PressedBrush : CardBrush;
        }
        if (opening)
        {
            // Move the hidden HWND once before showing it. WPF's subsequent resizes go through the
            // same native anchor below; there is no polling-time WPF Left/Top or native move loop.
            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            int width = (int)Math.Ceiling(Width * anchorScale), height = (int)Math.Ceiling(Height * anchorScale);
            SetWindowPos(hwnd, nint.Zero, work.Right - width - EdgeInset, work.Bottom - height - EdgeInset,
                width, height, 0x0010 | 0x0004); // NOACTIVATE | NOZORDER
            Show();
        }
    }

    void AnchorWindowPosition(nint hwnd, nint position)
    {
        if (anchorArea is not { } work || position == nint.Zero || !GetWindowRect(hwnd, out var current)) return;
        var move = Marshal.PtrToStructure<NativeWindowPos>(position);
        const uint noSize = 0x0001, noMove = 0x0002;
        int width = (move.Flags & noSize) == 0 ? move.Width : current.Right - current.Left;
        int height = (move.Flags & noSize) == 0 ? move.Height : current.Bottom - current.Top;
        if (width <= 0 || height <= 0) return;
        int x = work.Right - width - EdgeInset, y = work.Bottom - height - EdgeInset;
        if ((move.Flags & noMove) != 0 ? current.Left == x && current.Top == y : move.X == x && move.Y == y) return;
        move.X = x;
        move.Y = y;
        move.Flags &= ~noMove;
        Marshal.StructureToPtr(move, position, false);
    }

    sealed record Placement(OverlayEntry Entry, double X, double Y, double Width, double Height);
    sealed record Layout(double Width, double Height, double Font, double DeviceY,
        double? UpperCaptionY, double? LowerCaptionY, IReadOnlyList<Placement> Cards);
    const double DeviceWidth = 187.2, DeviceHeight = 108;
    const double CaptionHeight = 16, BankGap = 7;
    const double FrameWidth = 22, FrameHeight = 18;

    static TextBlock Text(string value, double size, Brush brush) => new()
    {
        Text = value, FontFamily = new FontFamily("Segoe UI"), FontSize = size, Foreground = brush,
        TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.None
    };
    static SolidColorBrush Paint(string value) => new((Color)ColorConverter.ConvertFromString(value));

    static double CardHeight(OverlayEntry entry, double width, double font)
    {
        var title = Text(entry.Title, Math.Max(9.5, font - 1.5), MutedBrush);
        title.Measure(new Size(width - 14, double.PositiveInfinity));
        var value = Text(entry.Value, font, ForegroundBrush);
        value.Measure(new Size(width - 14, double.PositiveInfinity));
        return Math.Ceiling(title.DesiredSize.Height + value.DesiredSize.Height + 10);
    }

    static Layout MakeLayout(IReadOnlyList<OverlayEntry> entries, double width, double font)
    {
        const double gap = 5;
        double cardWidth = (width - gap) / 2;
        List<Placement> placements = [];
        double y = 0;
        double? AddBank(bool left)
        {
            var bank = entries.Where(e => e.LeftSide == left).ToArray();
            if (bank.Length == 0) return null;
            double caption = y;
            y += CaptionHeight;
            for (int i = 0; i < bank.Length; i += 2)
            {
                double rowHeight = CardHeight(bank[i], cardWidth, font);
                if (i + 1 < bank.Length) rowHeight = Math.Max(rowHeight, CardHeight(bank[i + 1], cardWidth, font));
                placements.Add(new(bank[i], 0, y, cardWidth, rowHeight));
                if (i + 1 < bank.Length) placements.Add(new(bank[i + 1], cardWidth + gap, y, cardWidth, rowHeight));
                y += rowHeight + (i + 2 < bank.Length ? gap : 0);
            }
            return caption;
        }
        double? upper = AddBank(true);
        if (upper.HasValue) y += BankGap;
        double deviceY = y;
        y += DeviceHeight;
        if (entries.Any(e => !e.LeftSide)) y += BankGap;
        double? lower = AddBank(false);
        return new(width, y, font, deviceY, upper, lower, placements);
    }

    void Build(string profileName, int layer, bool both, IReadOnlyList<OverlayEntry> entries, double maxWidth, double maxHeight)
    {
        cards.Clear();
        maxWidth /= PreviewScale;
        maxHeight /= PreviewScale;
        double availableWidth = maxWidth - FrameWidth;
        string heading = both ? "BASE  ·  L1 + R1" : layer == 1 ? "L1  ·  LAYER 1" : layer == 2 ? "R1  ·  LAYER 2" : "BASE";
        var header = Text(heading + "  /  " + profileName, 12.5, ForegroundBrush);
        header.FontWeight = FontWeights.SemiBold;
        header.Margin = new Thickness(0, 0, 0, 7);
        var footer = Text((entries.Count == 0 ? "No bindings assigned to this layer\n" : "") +
            (both ? "R3 · Exit confirmation" : "L1 + R1 · Base bindings  /  + R3 · Exit confirmation"), 10.5, MutedBrush);
        footer.Margin = new Thickness(0, 6, 0, 0);
        double BodyBudget(double width)
        {
            var constraint = new Size(width, double.PositiveInfinity);
            header.Measure(constraint); footer.Measure(constraint);
            return Math.Max(1, maxHeight - FrameHeight - header.DesiredSize.Height - footer.DesiredSize.Height);
        }
        Layout? layout = null;
        // Ordinary profiles stay compact. Widen only when long chords or a small work area require it.
        foreach (double font in new[] { 11.5, 11, 10.5, 10 })
        {
            foreach (double desiredWidth in new[] { entries.Count == 0 ? 240d : 356d, 430d, 520d, 640d, 800d, 1000d, availableWidth })
            {
                double width = Math.Min(availableWidth, desiredWidth);
                var candidate = MakeLayout(entries, width, font);
                if (candidate.Height <= BodyBudget(width)) { layout = candidate; break; }
            }
            if (layout != null) break;
        }
        layout ??= MakeLayout(entries, availableWidth, 10);
        var stack = new StackPanel();
        stack.Children.Add(header);
        var canvas = new Canvas { Width = layout.Width, Height = layout.Height, ClipToBounds = false };
        double deviceX = (layout.Width - DeviceWidth) / 2;
        void DrawCaption(string title, double y)
        {
            var caption = Text(title, 9.5, MutedBrush);
            caption.FontWeight = FontWeights.SemiBold;
            Canvas.SetTop(caption, y); canvas.Children.Add(caption);
        }
        void Link(double x, double y1, double y2) => canvas.Children.Add(new Line { X1 = x, X2 = x, Y1 = y1, Y2 = y2, Stroke = StrokeBrush, StrokeThickness = 1 });
        if (layout.UpperCaptionY is { } upper)
        {
            DrawCaption("LEFT-SIDE BINDINGS", upper);
            Link(deviceX + 29, layout.DeviceY - BankGap, layout.DeviceY + 6);
        }
        if (layout.LowerCaptionY is { } lower)
        {
            DrawCaption("RIGHT-SIDE BINDINGS", lower);
            Link(deviceX + DeviceWidth - 29, layout.DeviceY + DeviceHeight - 5, lower);
        }
        var device = new Canvas { Width = 260, Height = 150 };
        DrawDevice(device, 0, 0);
        var deviceView = new Viewbox { Width = DeviceWidth, Height = DeviceHeight, Child = device };
        Canvas.SetLeft(deviceView, deviceX); Canvas.SetTop(deviceView, layout.DeviceY); canvas.Children.Add(deviceView);
        foreach (var placement in layout.Cards)
        {
            var content = new StackPanel();
            content.Children.Add(Text(placement.Entry.Title, Math.Max(9.5, layout.Font - 1.5), MutedBrush));
            content.Children.Add(Text(placement.Entry.Value, layout.Font, ForegroundBrush));
            var border = new Border
            {
                Width = placement.Width, Height = placement.Height, Padding = new Thickness(6, 4, 6, 4),
                Background = CardBrush, BorderBrush = StrokeBrush, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5), Child = content
            };
            Canvas.SetLeft(border, placement.X); Canvas.SetTop(border, placement.Y);
            canvas.Children.Add(border); cards[placement.Entry.Id] = border;
        }
        var view = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly,
            Child = canvas, MaxHeight = BodyBudget(layout.Width), MaxWidth = availableWidth };
        stack.Children.Add(view);
        stack.Children.Add(footer);
        root.Child = stack;
        double layoutWidth = Math.Min(maxWidth, layout.Width + FrameWidth);
        stack.Measure(new Size(layoutWidth - FrameWidth, double.PositiveInfinity));
        Width = layoutWidth * PreviewScale;
        Height = Math.Min(maxHeight, stack.DesiredSize.Height + FrameHeight) * PreviewScale;
        root.Measure(new Size(Width, Height));
    }

    static void DrawDevice(Canvas canvas, double x, double y)
    {
        void Put(FrameworkElement element, double px, double py) { Canvas.SetLeft(element, x + px); Canvas.SetTop(element, y + py); canvas.Children.Add(element); }
        Put(new Rectangle { Width = 47, Height = 10, RadiusX = 4, RadiusY = 4, Fill = StrokeBrush }, 14, 0);
        Put(new Rectangle { Width = 47, Height = 10, RadiusX = 4, RadiusY = 4, Fill = StrokeBrush }, 199, 0);
        Put(new Rectangle { Width = 260, Height = 140, RadiusX = 37, RadiusY = 37, Fill = Paint("#101315"), Stroke = StrokeBrush, StrokeThickness = 2 }, 0, 7);
        Put(new Rectangle { Width = 144, Height = 116, RadiusX = 8, RadiusY = 8, Fill = Paint("#090B0D"), Stroke = StrokeBrush, StrokeThickness = 1 }, 58, 19);
        Put(Text("VITA", 24, ForegroundBrush), 95, 46); Put(Text("PAD", 24, ForegroundBrush), 112, 73);
        Put(new Rectangle { Width = 10, Height = 35, Fill = StrokeBrush }, 27, 34);
        Put(new Rectangle { Width = 35, Height = 10, Fill = StrokeBrush }, 15, 46);
        foreach (double cx in new[] { 32d, 228d })
            Put(new Ellipse { Width = 31, Height = 31, Fill = CardBrush, Stroke = AccentBrush, StrokeThickness = 2 }, cx - 15.5, 92.5);
        foreach (var (symbol, px, py) in new[] { ("△", 220d, 23d), ("□", 205d, 39d), ("○", 235d, 39d), ("×", 221d, 54d) })
            Put(Text(symbol, 19, ForegroundBrush), px, py);
        Put(new Ellipse { Width = 6, Height = 6, Fill = MutedBrush }, 32, 17);
        Put(new Ellipse { Width = 6, Height = 6, Fill = MutedBrush }, 222, 17);
    }

    static double GetMonitorScale(int left, int top)
    {
        try
        {
            var monitor = MonitorFromPoint(new NativePoint(left + 1, top + 1), 2);
            if (GetDpiForMonitor(monitor, 0, out uint x, out _) == 0 && x > 0) return x / 96.0;
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        return 1;
    }
    [StructLayout(LayoutKind.Sequential)] readonly struct NativePoint(int x, int y) { public readonly int X = x, Y = y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct NativeWindowPos
    {
        public nint Hwnd, InsertAfter;
        public int X, Y, Width, Height;
        public uint Flags;
    }
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern nint MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);
}
