using System.Windows.Input;

namespace ControllerKeyboard;

internal enum ClipboardAction { None, Copy, Paste, SelectAll }

internal sealed record KeyFace(string Label, string? ShiftLabel = null, Key Special = Key.None,
    Key NumLockOffKey = Key.None, string? NumLockOffLabel = null, KeyFace? CircleLayer = null,
    ClipboardAction Clipboard = ClipboardAction.None)
{
    public bool IsBlank => Label.Length == 0;

    public string Display(bool shift, bool numLock)
    {
        if (!numLock && NumLockOffLabel is not null)
            return NumLockOffLabel;

        return shift && Special == Key.None && Clipboard == ClipboardAction.None ? ShiftLabel ?? Label.ToUpperInvariant() : Label;
    }
}

internal sealed record KeyboardLayout(KeyFace[,] Keys, int LeftColumns,
    (int Row, int Column) LeftAnchor, (int Row, int Column) RightAnchor)
{
    public int Rows => Keys.GetLength(0);
    public int Columns => Keys.GetLength(1);
}

internal static class KeyboardLayouts
{
    public static readonly KeyboardLayout Qwerty = new(new KeyFace[,]
    {
        { new("`", "~"), new("1", "!"), new("2", "@"), new("3", "#"), new("4", "$"), new("5", "%"),
          new("6", "^"), new("7", "&"), new("8", "*"), new("9", "("), new("0", ")"), new("-", "_") },
        { new("Tab", Special: Key.Tab), new("q"), new("w"), new("e"), new("r"), new("t"),
          new("y"), new("u"), new("i"), new("o"), new("p"), new("=", "+") },
        { new("Esc", Special: Key.Escape), new("a"), new("s"), new("d"), new("f"), new("g"),
          new("h"), new("j"), new("k"), new("l"), new(";", ":"), new("'", "\"") },
        { new("Ctrl", Special: Key.LeftCtrl), new("Alt", Special: Key.LeftAlt), new("z"), new("x"), new("c"), new("v"),
          new("b"), new("n"), new("m"), new(",", "<"), new(".", ">"), new("/", "?") }
    }, LeftColumns: 6, LeftAnchor: (2, 3), RightAnchor: (2, 8));

    public static readonly KeyboardLayout Symbols = CreateSymbols();

    private static KeyboardLayout CreateSymbols()
    {
        var blank = new KeyFace("");
        return new KeyboardLayout(new KeyFace[,]
        {
            { WithFunction("[", Key.F1), WithFunction("]", Key.F2), WithFunction("{", Key.F3),
              WithFunction("}", Key.F4), WithFunction("\\", Key.F5), WithFunction("|", Key.F6),
              new("Num Lock", Special: Key.NumLock), Number("7", Key.Home, "Home"), Number("8", Key.Up, "↑"),
              Number("9", Key.PageUp, "PgUp"), new("/"), new("*") },
            { WithFunction("", Key.F7), WithFunction("", Key.F8), WithFunction("", Key.F9),
              WithFunction("", Key.F10), WithFunction("", Key.F11), WithFunction("", Key.F12),
              blank, Number("4", Key.Left, "←"), new("5"), Number("6", Key.Right, "→"), new("+"), new("-") },
            { new("Esc", Special: Key.Escape), new("Delete", Special: Key.Delete),
              new("Copy", Clipboard: ClipboardAction.Copy), new("Paste", Clipboard: ClipboardAction.Paste),
              new("All", Clipboard: ClipboardAction.SelectAll), blank,
              blank, Number("1", Key.End, "End"), Number("2", Key.Down, "↓"), Number("3", Key.PageDown, "PgDn"), blank, blank },
            { new("Ctrl", Special: Key.LeftCtrl), new("Alt", Special: Key.LeftAlt), new("Shift", Special: Key.LeftShift),
              new("Ins", Special: Key.Insert), new("PrtSc", Special: Key.Snapshot), blank,
              blank, blank, Number("0", Key.Insert, "Ins"), Number(".", Key.Delete, "Del"), new("Enter", Special: Key.Enter), blank }
        }, LeftColumns: 6, LeftAnchor: (0, 2), RightAnchor: (1, 8));
    }

    private static KeyFace Number(string label, Key navigation, string navigationLabel) =>
        new(label, NumLockOffKey: navigation, NumLockOffLabel: navigationLabel);

    private static KeyFace WithFunction(string label, Key function) =>
        new(label, CircleLayer: new(function.ToString(), Special: function));
}
