using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace ControllerKeyboard;

public partial class MainWindow : Window
{
    private readonly XInputReader controller = new();
    private readonly ControllerInput input = new();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly DispatcherTimer pollTimer;
    private readonly DispatcherTimer pressReleaseTimer;
    private readonly bool desktop;
    private readonly KeyboardSounds? sounds;
    private readonly KeyboardPage qwerty;
    private readonly KeyboardPage symbols;
    private KeyboardPage page;
    private bool circleHeld;
    private bool circleUsed;
    private bool lastCircleHeld;
    private bool keyboardShift;
    private readonly ModifierLatch shiftLatch = new();
    private readonly ModifierLatch controlLatch = new();
    private readonly ModifierLatch altLatch = new();
    private readonly ModifierLatch alternateLatch = new();
    private bool numLock = true;
    private bool connected;
    private bool keyboardOpen = true;
    private KeyFace? repeatFace;
    private nint repeatTarget;
    private readonly DesktopOverlayPresenter? desktopOverlay;
    private bool overlayPositioned;
    private Button? pressedButton;
    private KeyFace? pressedFace;
    private long pressStarted;
    private int sizePercent = 100;
    private int opacityPercent = 100;
    private bool r1Held;
    private readonly SolidColorBrush fadedMask = new(Colors.White);
    private readonly RectangleGeometry opaqueKeyArea = new();
    private readonly RectangleGeometry opaqueControlArea = new();
    private DrawingBrush? overlayMask;
    private bool resourcesReleased;

    internal event EventHandler? QuitRequested;
    internal event Action? KeyboardClosed;
    internal bool KeyboardVisible => keyboardOpen;

    private bool ShowingCircleLayer => ReferenceEquals(page, symbols) && (circleHeld || alternateLatch.Active);
    private bool ShiftActive => keyboardShift || shiftLatch.Active || (ReferenceEquals(page, qwerty) && circleHeld);
    private bool ControlActive => controlLatch.Active;
    private bool AltActive => altLatch.Active;
    private bool NumpadDigits => numLock && !ShowingCircleLayer;
    private double OverlayHeight => KeyboardPages.Height + KeyboardPanel.Padding.Top + KeyboardPanel.Padding.Bottom
        + KeyboardPanel.BorderThickness.Top + KeyboardPanel.BorderThickness.Bottom;

    public MainWindow() : this(desktop: false) { }

    internal MainWindow(bool desktop)
    {
        this.desktop = desktop;
        InitializeComponent();
        pressReleaseTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher);
        pressReleaseTimer.Tick += (_, _) => ClearKeyPress();
        if (desktop)
        {
            ConfigureOverlay();
            desktopOverlay = new DesktopOverlayPresenter(Surface);
            desktopOverlay.PanelClicked += HandlePanelClick;
            keyboardOpen = false;
            sounds = new KeyboardSounds();
        }
        qwerty = BuildPage(KeyboardLayouts.Qwerty);
        symbols = BuildPage(KeyboardLayouts.Symbols);
        page = qwerty;
        symbols.Panel.Visibility = Visibility.Collapsed;
        RefreshPage();

        pollTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(8)
        };
        pollTimer.Tick += (_, _) => PollController();
        Loaded += (_, _) =>
        {
            if (!desktop)
            {
                Editor.Focus();
                StartListening();
            }
        };
        Closed += (_, _) => ReleaseResources();
        Deactivated += (_, _) =>
        {
            if (desktop)
                return;
            input.Reset();
            ResetModifiers();
            keyboardShift = false;
            repeatFace = null;
            ClearKeyPress();
            RefreshPage();
        };
        PreviewKeyDown += Window_KeyDown;
        PreviewKeyUp += Window_KeyUp;
    }

    internal void StartListening()
    {
        if (!desktop)
            pollTimer.Start();
    }

    internal void ReleaseResources()
    {
        if (resourcesReleased)
            return;

        resourcesReleased = true;
        pollTimer.Stop();
        desktopOverlay?.Dispose();
        ClearKeyPress();
        sounds?.Dispose();
    }

    internal void ToggleKeyboard() => SetKeyboardOpen(!keyboardOpen);

    internal void OpenFromMapper()
    {
        SetKeyboardOpen(true);
        // Establish the neutral baseline before the first frame from the host.
        input.Update(default, clock.Elapsed.TotalMilliseconds, keyboardOpen);
    }

    internal void CloseFromMapper() => SetKeyboardOpen(false);

    private void HandlePanelClick(Point point)
    {
        if (TouchHide.Visibility == Visibility.Visible)
        {
            Point origin = TouchHide.TranslatePoint(default, Surface);
            if (new Rect(origin, TouchHide.RenderSize).Contains(point))
            {
                SetKeyboardOpen(false);
                return;
            }
        }
        TouchHide.Visibility = Visibility.Visible;
        desktopOverlay?.Invalidate();
        RenderDesktopFrame();
    }

    internal void AcceptControllerState(PadState state)
    {
        ApplyPadFrame(input.Update(state, clock.Elapsed.TotalMilliseconds, keyboardOpen));
        RenderDesktopFrame();
    }

    private void ConfigureOverlay()
    {
        Surface.Margin = new Thickness(0);
        KeyboardPanel.Margin = new Thickness(0);
        TestHeader.Visibility = Visibility.Collapsed;
        TestEditor.Visibility = Visibility.Collapsed;
        EditorRow.MinHeight = 0;
        EditorRow.Height = new GridLength(0);
        Content = null;
        Surface.Width = KeyboardPanel.MaxWidth;
        Surface.Height = OverlayHeight;
        Surface.IsHitTestVisible = false;
        Surface.UseLayoutRounding = UseLayoutRounding;
        TextElement.SetFontFamily(Surface, FontFamily);
        TextElement.SetForeground(Surface, Foreground);
        TextOptions.SetTextFormattingMode(Surface, TextOptions.GetTextFormattingMode(this));
        var designBounds = new Rect(0, 0, Surface.Width, Surface.Height);
        var maskDrawing = new DrawingGroup();
        maskDrawing.Children.Add(new GeometryDrawing(fadedMask, null, new RectangleGeometry(designBounds)));
        maskDrawing.Children.Add(new GeometryDrawing(Brushes.White, null, opaqueKeyArea));
        maskDrawing.Children.Add(new GeometryDrawing(Brushes.White, null, opaqueControlArea));
        overlayMask = new DrawingBrush(maskDrawing)
        {
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = designBounds,
            Stretch = Stretch.Fill
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint handle = new WindowInteropHelper(this).Handle;
        const int useImmersiveDarkMode = 20;
        int enabled = 1;
        DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private KeyboardPage BuildPage(KeyboardLayout layout)
    {
        var keyboard = new KeyboardPage(layout);
        keyboard.Panel.ColumnDefinitions.Add(new ColumnDefinition());
        keyboard.Panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
        keyboard.Panel.ColumnDefinitions.Add(new ColumnDefinition());
        BuildHalf(keyboard, 0, layout.LeftColumns, 0);
        BuildHalf(keyboard, layout.LeftColumns, layout.Columns, 2);

        var divider = new Border { Width = 1, Margin = new Thickness(0, 2, 0, 2), Background = BrushFor("KeyOutline") };
        Grid.SetColumn(divider, 1);
        keyboard.Panel.Children.Add(divider);
        KeyboardPages.Children.Add(keyboard.Panel);
        return keyboard;
    }

    private void BuildHalf(KeyboardPage keyboard, int start, int end, int panelColumn)
    {
        KeyboardLayout layout = keyboard.Layout;
        var half = new Grid();
        for (int r = 0; r < layout.Rows; r++)
            half.RowDefinitions.Add(new RowDefinition());
        for (int c = start; c < end; c++)
            half.ColumnDefinitions.Add(new ColumnDefinition());

        var style = (Style)FindResource("KeyButton");
        for (int r = 0; r < layout.Rows; r++)
        {
            for (int c = start; c < end; c++)
            {
                KeyFace face = layout.Keys[r, c];
                if (face.IsBlank && face.CircleLayer is null)
                    continue;

                int keyRow = r;
                int keyColumn = c;
                var button = new Button { Style = style };
                button.Click += (_, _) =>
                {
                    Highlight(keyRow, keyColumn);
                    TypeFace(ActiveFace(keyboard.Layout.Keys[keyRow, keyColumn]));
                };

                keyboard.Buttons.Add(face, button);
                Grid.SetRow(button, r);
                Grid.SetColumn(button, c - start);
                half.Children.Add(button);
            }
        }

        Grid.SetColumn(half, panelColumn);
        keyboard.Panel.Children.Add(half);
    }

    private void PollController()
    {
        double now = clock.Elapsed.TotalMilliseconds;
        bool hasController = controller.TryRead((long)now, out PadState state, out bool newController);
        if (hasController != connected)
        {
            connected = hasController;
            ControllerStatus.Text = connected ? "XInput controller connected" : "No XInput controller";
            ControllerDot.Fill = BrushFor(connected ? "SelectedFill" : "SecondaryText");
            if (!connected)
            {
                input.Reset();
                ResetModifiers();
                repeatFace = null;
                r1Held = false;
                ClearKeyPress();
                RefreshPage();
            }
        }

        if (connected && (desktop || IsActive))
        {
            if (newController)
                input.Reset();
            ApplyPadFrame(input.Update(state, now, desktop && keyboardOpen));
        }
        RenderDesktopFrame();
    }

    private void ApplyPadFrame(PadFrame frame)
    {
        if (desktop && frame.Overlay != OverlayCommand.None)
        {
            switch (frame.Overlay)
            {
                case OverlayCommand.Reset:
                    ResetOverlay();
                    break;
                case OverlayCommand.Toggle:
                    ToggleKeyboard();
                    break;
                case OverlayCommand.Quit when keyboardOpen:
                    QuitRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
            return;
        }

        if (!keyboardOpen)
        {
            if (!desktop && frame.Cross && !frame.CaretMode)
                SetKeyboardOpen(true);
            return;
        }

        if (frame.Exit)
        {
            SetKeyboardOpen(false);
            return;
        }

        if (frame.CircleHeld && !lastCircleHeld)
            sounds?.Toggle();
        lastCircleHeld = frame.CircleHeld;

        if (r1Held != frame.CaretMode)
        {
            r1Held = frame.CaretMode;
            PaintKey(page.Highlighted, page.Buttons[page.Highlighted]);
            desktopOverlay?.Invalidate();
        }

        if (desktop)
        {
            AdjustOverlay(frame.Adjustment);
            desktopOverlay!.Move(frame.MoveX, frame.MoveY);
        }
        if (!frame.CrossHeld)
            ReleaseKeyPress();

        bool circleChanged = circleHeld != frame.CircleHeld;
        if (circleChanged && frame.CircleHeld)
            circleUsed = false;
        circleHeld = frame.CircleHeld;
        if (frame.CircleTap && !circleUsed)
            (ReferenceEquals(page, qwerty) ? shiftLatch : alternateLatch).Tap(frame.Timestamp);

        if (frame.SwapLayout)
        {
            circleUsed |= circleHeld;
            SwitchLayout();
        }
        else if (circleChanged || frame.CircleTap)
            RefreshPage();

        if (frame.JumpRight)
            Highlight(page.Layout.RightAnchor.Row, page.Layout.RightAnchor.Column);
        else if (frame.JumpLeft)
            Highlight(page.Layout.LeftAnchor.Row, page.Layout.LeftAnchor.Column);

        if (frame.CaretMode)
        {
            repeatFace = null;
            ClearKeyPress();
            MoveCaret(frame.Direction, frame.SelectText);
            if (frame.Backspace)
                Backspace(word: true);
            if (frame.Cross)
                ExecuteKey(Key.Enter);
            if (frame.Paste)
                ClipboardShortcut(Key.V, ApplicationCommands.Paste);
            else if (frame.Triangle)
                ClipboardShortcut(Key.C, ApplicationCommands.Copy);
            return;
        }

        MoveHighlight(frame.Direction);
        if (frame.Backspace)
            Backspace();
        if (frame.Triangle)
            TypeText(" ");
        if (frame.Cross)
        {
            KeyFace face = ActiveFace(page.Highlighted);
            ShowKeyPress(page.Buttons[page.Highlighted], face);
            repeatFace = IsRepeatable(face) ? face : null;
            if (desktop)
                repeatTarget = DesktopKeyboard.CurrentTarget();
            TypeFace(face, frame.Timestamp);
        }
        else if (frame.CrossRepeat && repeatFace is not null)
        {
            if (!desktop || (repeatTarget != 0 && DesktopKeyboard.CurrentTarget() == repeatTarget))
                TypeFace(repeatFace);
            else
                repeatFace = null;
        }
    }

    private void SwitchLayout()
    {
        repeatFace = null;
        ClearKeyPress();
        page.Panel.Visibility = Visibility.Collapsed;
        page = ReferenceEquals(page, qwerty) ? symbols : qwerty;
        page.Panel.Visibility = Visibility.Visible;
        RefreshPage();
        sounds?.Toggle();
    }

    private void ShowKeyPress(Button button, KeyFace face)
    {
        ClearKeyPress();
        pressedButton = button;
        pressedFace = face;
        pressStarted = clock.ElapsedMilliseconds;
        button.Tag = "ControllerPressed";
        UpdateOpacityMask();
    }

    private void ReleaseKeyPress()
    {
        if (pressedButton is null || pressReleaseTimer.IsEnabled)
            return;

        long remaining = 65 - (clock.ElapsedMilliseconds - pressStarted);
        if (remaining <= 0)
            ClearKeyPress();
        else
        {
            pressReleaseTimer.Interval = TimeSpan.FromMilliseconds(remaining);
            pressReleaseTimer.Start();
        }
    }

    private void ClearKeyPress()
    {
        pressReleaseTimer.Stop();
        pressedButton?.ClearValue(TagProperty);
        pressedButton = null;
        pressedFace = null;
        UpdateOpacityMask();
    }

    private void MoveCaret(PadButtons direction, bool selectText)
    {
        Key key = direction switch
        {
            PadButtons.Up => Key.Up,
            PadButtons.Down => Key.Down,
            PadButtons.Left => Key.Left,
            PadButtons.Right => Key.Right,
            _ => Key.None
        };
        if (key != Key.None)
            ExecuteKey(key, selectText);
    }

    private void MoveHighlight(PadButtons direction)
    {
        (int rowStep, int columnStep) = direction switch
        {
            PadButtons.Up => (-1, 0),
            PadButtons.Down => (1, 0),
            PadButtons.Left => (0, -1),
            PadButtons.Right => (0, 1),
            _ => (0, 0)
        };
        if (direction == 0)
            return;

        for (int r = page.Row + rowStep, c = page.Column + columnStep;
             r >= 0 && r < page.Layout.Rows && c >= 0 && c < page.Layout.Columns;
             r += rowStep, c += columnStep)
        {
            KeyFace position = page.Layout.Keys[r, c];
            if (!ActiveFace(position).IsBlank)
            {
                Highlight(r, c);
                return;
            }
        }
    }

    private void Highlight(int row, int column)
    {
        KeyFace previous = page.Highlighted;
        page.Row = row;
        page.Column = column;
        if (ReferenceEquals(previous, page.Highlighted))
            return;

        repeatFace = null;
        ClearKeyPress();
        PaintKey(previous, page.Buttons[previous]);
        PaintKey(page.Highlighted, page.Buttons[page.Highlighted]);
        UpdateOpacityMask();
    }

    private Brush BrushFor(string resource) => (Brush)FindResource(resource);

    private bool IsLatched(Key key) => key switch
    {
        Key.LeftCtrl => ControlActive,
        Key.LeftAlt => AltActive,
        Key.LeftShift => shiftLatch.Active,
        Key.NumLock => numLock,
        _ => false
    };

    private bool IsLocked(Key key) => key switch
    {
        Key.LeftCtrl => controlLatch.Locked,
        Key.LeftAlt => altLatch.Locked,
        Key.LeftShift => shiftLatch.Locked,
        _ => false
    };

    private void PaintKey(KeyFace face, Button button)
    {
        if (ActiveFace(face).IsBlank)
        {
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = BrushFor("SecondaryText");
            return;
        }

        bool selected = ReferenceEquals(face, page.Highlighted);
        bool latched = IsLatched(face.Special);
        string selectedFill = r1Held ? "DimmedSelectedFill" : "SelectedFill";
        button.Background = BrushFor(selected ? selectedFill : latched ? "LatchedFill" : "KeyFill");
        button.BorderBrush = BrushFor(selected ? selectedFill : latched ? "LatchedOutline" : "KeyOutline");
        button.Foreground = BrushFor(selected ? "SelectedText" : "MainText");
    }

    private KeyFace ActiveFace(KeyFace face) => ShowingCircleLayer ? face.CircleLayer ?? face : face;

    private void RefreshPage()
    {
        if (ActiveFace(page.Highlighted).IsBlank)
        {
            var (row, column) = page.Layout.LeftAnchor;
            page.Row = row;
            if (ActiveFace(page.Highlighted).IsBlank)
                page.Column = column;
        }

        if (!ReferenceEquals(repeatFace, ActiveFace(page.Highlighted)))
            repeatFace = null;
        if (pressedFace is not null && !ReferenceEquals(pressedFace, ActiveFace(page.Highlighted)))
            ClearKeyPress();

        bool shifted = ShiftActive;
        foreach ((KeyFace position, Button button) in page.Buttons)
        {
            KeyFace face = ActiveFace(position);
            string primary = face.Display(shifted, NumpadDigits);
            string? corner = CornerLabel(position, shifted);

            bool latched = IsLatched(face.Special);
            button.Content = new KeyLabels(primary, corner, latched, IsLocked(face.Special));
            button.FontSize = primary.Length > 1 ? 15 : 22;
            button.IsEnabled = !face.IsBlank;
            AutomationProperties.SetName(button, primary);
            PaintKey(position, button);
        }
        UpdateOpacityMask();
    }

    private void UpdateOpacityMask()
    {
        desktopOverlay?.Invalidate();
        if (!desktop || opacityPercent == 100)
        {
            Surface.OpacityMask = null;
            return;
        }

        fadedMask.Opacity = opacityPercent / 100d;
        Button button = page.Buttons[page.Highlighted];
        if (button.ActualWidth <= 0 || button.ActualHeight <= 0)
            return;

        Point origin = button.TranslatePoint(new Point(0, 0), Surface);
        double scale = ReferenceEquals(button, pressedButton) ? 0.87 : 1;
        double width = button.ActualWidth * scale;
        double height = button.ActualHeight * scale;
        opaqueKeyArea.Rect = new Rect(origin.X + (button.ActualWidth - width) / 2,
            origin.Y + (button.ActualHeight - height) / 2, width, height);
        opaqueKeyArea.RadiusX = opaqueKeyArea.RadiusY = 8 * scale;
        opaqueControlArea.Rect = TouchHide.Visibility == Visibility.Visible
            ? new Rect(TouchHide.TranslatePoint(default, Surface), TouchHide.RenderSize) : Rect.Empty;
        opaqueControlArea.RadiusX = opaqueControlArea.RadiusY = 16;
        Surface.OpacityMask = overlayMask;
    }

    private string? CornerLabel(KeyFace position, bool shifted)
    {
        if (position.CircleLayer is not null)
            return ShowingCircleLayer ? position.Label : position.CircleLayer.Label;

        if (position.NumLockOffLabel is not null)
            return NumpadDigits ? position.NumLockOffLabel : position.Label;

        if (position.ShiftLabel is not null)
            return shifted ? position.Label : position.ShiftLabel;

        return null;
    }

    private bool IsRepeatable(KeyFace face)
    {
        Key key = SpecialKey(face);
        return face.Clipboard == ClipboardAction.None &&
            key is not (Key.LeftCtrl or Key.LeftAlt or Key.LeftShift or Key.NumLock or Key.Escape or Key.Insert);
    }

    private Key SpecialKey(KeyFace face) =>
        !NumpadDigits && face.NumLockOffKey != Key.None ? face.NumLockOffKey : face.Special;

    private void TypeFace(KeyFace face, double? tapTime = null)
    {
        switch (face.Clipboard)
        {
            case ClipboardAction.Copy:
                ClipboardShortcut(Key.C, ApplicationCommands.Copy);
                return;
            case ClipboardAction.Paste:
                ClipboardShortcut(Key.V, ApplicationCommands.Paste);
                return;
            case ClipboardAction.SelectAll:
                ClipboardShortcut(Key.A, ApplicationCommands.SelectAll);
                return;
        }

        Key special = SpecialKey(face);
        if (special == Key.None)
            TypeText(face.Display(ShiftActive, NumpadDigits));
        else
            ExecuteKey(special, tapTime: tapTime);
    }

    private void ExecuteKey(Key key, bool selectText = false, double? tapTime = null)
    {
        ModifierLatch? latch = key switch
        {
            Key.LeftCtrl => controlLatch,
            Key.LeftAlt => altLatch,
            Key.LeftShift => shiftLatch,
            _ => null
        };
        if (latch is not null)
        {
            latch.Tap(tapTime ?? clock.Elapsed.TotalMilliseconds);
            RefreshPage();
            sounds?.Toggle();
            return;
        }

        if (key == Key.NumLock)
        {
            numLock = !numLock;
            RefreshPage();
            sounds?.Toggle();
            return;
        }

        if (desktop)
        {
            SendDesktopKey(key, ActiveModifiers(selectText));
            return;
        }

        switch (key)
        {
            case Key.Escape:
                SetKeyboardOpen(false);
                return;
            case Key.Enter:
                ReplaceSelection(Environment.NewLine);
                ConsumeModifiers();
                return;
            case Key.Tab:
                ReplaceSelection("\t");
                ConsumeModifiers();
                return;
            case >= Key.F1 and <= Key.F12:
                SendFunctionKey(key);
                ConsumeModifiers();
                return;
        }

        RoutedCommand? command = (key, ShiftActive || selectText, ControlActive) switch
        {
            (Key.Up, true, true) => EditingCommands.SelectUpByParagraph,
            (Key.Up, true, false) => EditingCommands.SelectUpByLine,
            (Key.Up, false, _) => ControlActive ? EditingCommands.MoveUpByParagraph : EditingCommands.MoveUpByLine,
            (Key.Down, true, true) => EditingCommands.SelectDownByParagraph,
            (Key.Down, true, false) => EditingCommands.SelectDownByLine,
            (Key.Down, false, _) => ControlActive ? EditingCommands.MoveDownByParagraph : EditingCommands.MoveDownByLine,
            (Key.Left, true, true) => EditingCommands.SelectLeftByWord,
            (Key.Left, true, false) => EditingCommands.SelectLeftByCharacter,
            (Key.Left, false, _) => ControlActive ? EditingCommands.MoveLeftByWord : EditingCommands.MoveLeftByCharacter,
            (Key.Right, true, true) => EditingCommands.SelectRightByWord,
            (Key.Right, true, false) => EditingCommands.SelectRightByCharacter,
            (Key.Right, false, _) => ControlActive ? EditingCommands.MoveRightByWord : EditingCommands.MoveRightByCharacter,
            (Key.Home, true, true) => EditingCommands.SelectToDocumentStart,
            (Key.Home, true, false) => EditingCommands.SelectToLineStart,
            (Key.Home, false, _) => ControlActive ? EditingCommands.MoveToDocumentStart : EditingCommands.MoveToLineStart,
            (Key.End, true, true) => EditingCommands.SelectToDocumentEnd,
            (Key.End, true, false) => EditingCommands.SelectToLineEnd,
            (Key.End, false, _) => ControlActive ? EditingCommands.MoveToDocumentEnd : EditingCommands.MoveToLineEnd,
            (Key.PageUp, _, _) => ShiftActive ? EditingCommands.SelectUpByPage : EditingCommands.MoveUpByPage,
            (Key.PageDown, _, _) => ShiftActive ? EditingCommands.SelectDownByPage : EditingCommands.MoveDownByPage,
            (Key.Delete, _, _) => ControlActive ? EditingCommands.DeleteNextWord : EditingCommands.Delete,
            (Key.Insert, _, true) => ApplicationCommands.Copy,
            (Key.Insert, true, false) => ApplicationCommands.Paste,
            (Key.Insert, false, false) => EditingCommands.ToggleInsert,
            _ => null
        };
        command?.Execute(null, Editor);
        ConsumeModifiers();
    }

    private void SendFunctionKey(Key key)
    {
        if (PresentationSource.FromVisual(Editor) is not PresentationSource source)
            return;

        var down = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key);
        down.RoutedEvent = Keyboard.PreviewKeyDownEvent;
        Editor.RaiseEvent(down);
        down.RoutedEvent = Keyboard.KeyDownEvent;
        Editor.RaiseEvent(down);

        var up = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key);
        up.RoutedEvent = Keyboard.PreviewKeyUpEvent;
        Editor.RaiseEvent(up);
        up.RoutedEvent = Keyboard.KeyUpEvent;
        Editor.RaiseEvent(up);
    }

    private void TypeText(string text)
    {
        if (desktop)
        {
            if (DesktopKeyboard.SendText(DesktopKeyboard.CurrentTarget(), text, ActiveModifiers()))
            {
                sounds?.Key();
                ConsumeModifiers();
            }
            return;
        }

        if (!ControlActive && !AltActive)
        {
            ReplaceSelection(text);
            ConsumeModifiers();
            return;
        }

        if (!ControlActive || AltActive)
        {
            ConsumeModifiers();
            return;
        }

        RoutedCommand? command = text.ToLowerInvariant() switch
        {
            "a" => ApplicationCommands.SelectAll,
            "c" => ApplicationCommands.Copy,
            "x" => ApplicationCommands.Cut,
            "v" => ApplicationCommands.Paste,
            "z" => ShiftActive ? ApplicationCommands.Redo : ApplicationCommands.Undo,
            "y" => ApplicationCommands.Redo,
            _ => null
        };
        command?.Execute(null, Editor);
        ConsumeModifiers();
    }

    private void Backspace(bool word = false)
    {
        if (desktop)
        {
            ModifierKeys modifiers = ActiveModifiers();
            if (word) modifiers |= ModifierKeys.Control;
            if (DesktopKeyboard.SendKey(DesktopKeyboard.CurrentTarget(), Key.Back, modifiers))
            {
                sounds?.Backspace();
                ConsumeModifiers();
            }
            return;
        }
        if (word)
            EditingCommands.DeletePreviousWord.Execute(null, Editor);
        else if (AltActive)
            (ShiftActive ? ApplicationCommands.Redo : ApplicationCommands.Undo).Execute(null, Editor);
        else if (ControlActive)
            EditingCommands.DeletePreviousWord.Execute(null, Editor);
        else
            DeleteBackward();
        ConsumeModifiers();
    }

    private ModifierKeys ActiveModifiers(bool selectText = false)
    {
        ModifierKeys modifiers = ModifierKeys.None;
        if (ControlActive) modifiers |= ModifierKeys.Control;
        if (AltActive) modifiers |= ModifierKeys.Alt;
        if (ShiftActive || selectText) modifiers |= ModifierKeys.Shift;
        return modifiers;
    }

    private void SendDesktopKey(Key key, ModifierKeys modifiers)
    {
        if (DesktopKeyboard.SendKey(DesktopKeyboard.CurrentTarget(), key, modifiers))
        {
            sounds?.Key();
            ConsumeModifiers();
        }
    }

    private void ClipboardShortcut(Key key, RoutedCommand localCommand)
    {
        if (desktop)
            SendDesktopKey(key, ActiveModifiers() | ModifierKeys.Control);
        else
        {
            localCommand.Execute(null, Editor);
            ConsumeModifiers();
        }
    }

    private void ConsumeModifiers()
    {
        circleUsed |= circleHeld;
        bool changed = shiftLatch.Consume() | controlLatch.Consume() | altLatch.Consume() | alternateLatch.Consume();
        if (changed)
            RefreshPage();
    }

    private void ResetModifiers()
    {
        circleHeld = circleUsed = lastCircleHeld = false;
        shiftLatch.Reset();
        controlLatch.Reset();
        altLatch.Reset();
        alternateLatch.Reset();
    }

    private void ReplaceSelection(string text)
    {
        int start = Editor.SelectionStart;
        Editor.SelectedText = text;
        Editor.Select(start + text.Length, 0);
        Editor.Focus();
    }

    private void DeleteBackward()
    {
        if (Editor.SelectionLength == 0)
        {
            int caret = Editor.CaretIndex;
            if (caret == 0)
                return;

            int[] elements = StringInfo.ParseCombiningCharacters(Editor.Text);
            int previous = Array.BinarySearch(elements, caret);
            previous = previous >= 0 ? previous - 1 : ~previous - 1;
            int start = elements[previous];
            Editor.Select(start, caret - start);
        }

        ReplaceSelection("");
    }

    private void SetKeyboardOpen(bool open)
    {
        bool wasOpen = keyboardOpen;
        repeatFace = null;
        ClearKeyPress();
        keyboardOpen = open;
        TouchHide.Visibility = Visibility.Collapsed;
        r1Held = false;
        input.Reset();
        ResetModifiers();
        if (desktop)
        {
            if (open)
            {
                RefreshPage();
                LayoutDesktopSurface();
                keyboardOpen = desktopOverlay!.Show(DesktopKeyboard.CurrentTarget(), overlayPositioned,
                    sizePercent, clock.Elapsed.TotalMilliseconds);
                sizePercent = Math.Min(sizePercent, desktopOverlay.MaximumSizePercent);
                overlayPositioned = keyboardOpen;
            }
            else
            {
                desktopOverlay!.Hide();
                if (wasOpen) KeyboardClosed?.Invoke();
            }
            return;
        }

        if (open)
            RefreshPage();
        KeyboardPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        ClosedPanel.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        Editor.Focus();
    }

    private void AdjustOverlay(PadButtons adjustment)
    {
        switch (adjustment)
        {
            case PadButtons.Up or PadButtons.Down:
                int maximum = desktopOverlay!.MaximumSizePercent;
                int updated = Math.Clamp(sizePercent + (adjustment == PadButtons.Up ? 5 : -5), 5, maximum);
                if (updated != sizePercent)
                {
                    sizePercent = updated;
                    desktopOverlay.Resize(sizePercent, clock.Elapsed.TotalMilliseconds);
                }
                break;
            case PadButtons.Left or PadButtons.Right:
                opacityPercent = Math.Clamp(opacityPercent + (adjustment == PadButtons.Right ? 5 : -5), 5, 100);
                UpdateOpacityMask();
                break;
        }
    }

    private void LayoutDesktopSurface()
    {
        bool invalid = !Surface.IsMeasureValid || !Surface.IsArrangeValid;
        if (!invalid && desktopOverlay?.NeedsRedraw != true)
            return;

        if (invalid)
        {
            var size = new Size(Surface.Width, Surface.Height);
            Surface.Measure(size);
            Surface.Arrange(new Rect(size));
        }
        // Template children can be invalidated while the root's layout is still valid.
        Surface.UpdateLayout();
        if (invalid || opacityPercent < 100)
            UpdateOpacityMask();
    }

    private void RenderDesktopFrame()
    {
        if (desktopOverlay is null || !keyboardOpen)
            return;
        LayoutDesktopSurface();
        desktopOverlay.Present(clock.Elapsed.TotalMilliseconds);
    }

    private void ResetOverlay()
    {
        sizePercent = opacityPercent = 100;
        UpdateOpacityMask();
        overlayPositioned = keyboardOpen;
        if (keyboardOpen)
        {
            LayoutDesktopSurface();
            desktopOverlay!.Show(DesktopKeyboard.CurrentTarget(), retain: false, sizePercent, clock.Elapsed.TotalMilliseconds);
        }
    }

    private void OpenKeyboard_Click(object sender, RoutedEventArgs e) => SetKeyboardOpen(true);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftShift or Key.RightShift)
        {
            keyboardShift = true;
            RefreshPage();
        }
        else if (e.Key == Key.Escape && keyboardOpen)
        {
            SetKeyboardOpen(false);
            e.Handled = true;
        }
        else if (e.Key == Key.F2 && !keyboardOpen)
        {
            SetKeyboardOpen(true);
            e.Handled = true;
        }
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.LeftShift or Key.RightShift))
            return;

        keyboardShift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        RefreshPage();
    }

    private sealed class KeyboardPage(KeyboardLayout layout)
    {
        public KeyboardLayout Layout { get; } = layout;
        public Grid Panel { get; } = new();
        public Dictionary<KeyFace, Button> Buttons { get; } = new(ReferenceEqualityComparer.Instance);
        public int Row { get; set; } = layout.LeftAnchor.Row;
        public int Column { get; set; } = layout.LeftAnchor.Column;
        public KeyFace Highlighted => Layout.Keys[Row, Column];
    }

    private sealed record KeyLabels(string Primary, string? Corner, bool Latched, bool Locked)
    {
        public double CornerFontSize => Corner?.Length > 2 ? 8 : 10.5;
    }
}

