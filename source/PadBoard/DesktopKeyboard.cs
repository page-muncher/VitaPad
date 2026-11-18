using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace ControllerKeyboard;

internal static class DesktopKeyboard
{
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const uint Extended = 0x0001;
    private const int ShiftVk = 0x10;
    private const int ControlVk = 0x11;
    private const int AltVk = 0x12;

    public static nint CurrentTarget()
    {
        nint window = GetForegroundWindow();
        if (window == 0 || window == GetShellWindow())
            return 0;

        GetWindowThreadProcessId(window, out uint process);
        if (process == Environment.ProcessId)
            return 0;

        var windowClass = new StringBuilder(64);
        GetClassName(window, windowClass, windowClass.Capacity);
        return windowClass.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW" ? 0 : window;
    }

    public static bool SendKey(nint target, Key key, ModifierKeys modifiers)
    {
        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        return virtualKey != 0 && SendVirtualKey(target, (ushort)virtualKey, modifiers);
    }

    public static bool SendText(nint target, string text, ModifierKeys modifiers)
    {
        if (target == 0 || text.Length == 0)
            return false;

        if ((modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            uint thread = GetWindowThreadProcessId(target, out _);
            short mapped = VkKeyScanEx(text[0], GetKeyboardLayout(thread));
            if (mapped == -1)
                return false;

            int state = (mapped >> 8) & 0xff;
            if ((state & 1) != 0) modifiers |= ModifierKeys.Shift;
            if ((state & 2) != 0) modifiers |= ModifierKeys.Control;
            if ((state & 4) != 0) modifiers |= ModifierKeys.Alt;
            return SendVirtualKey(target, (ushort)(mapped & 0xff), modifiers);
        }

        if (text == " ")
            return SendKey(target, Key.Space, modifiers);

        var events = new Input[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            events[i * 2] = Input.Keyboard(0, text[i], Unicode);
            events[i * 2 + 1] = Input.Keyboard(0, text[i], Unicode | KeyUp);
        }

        return Send(target, events);
    }

    private static bool SendVirtualKey(nint target, ushort key, ModifierKeys modifiers)
    {
        var events = new List<Input>(8);
        AddModifier(events, modifiers, ModifierKeys.Control, ControlVk);
        AddModifier(events, modifiers, ModifierKeys.Shift, ShiftVk);
        AddModifier(events, modifiers, ModifierKeys.Alt, AltVk);

        int modifierCount = events.Count;
        uint flags = key is >= 0x21 and <= 0x28 or 0x2c or 0x2d or 0x2e or 0x6f or 0x90 ? Extended : 0;
        events.Add(Input.Keyboard(key, 0, flags));
        events.Add(Input.Keyboard(key, 0, flags | KeyUp));
        for (int i = modifierCount - 1; i >= 0; i--)
            events.Add(events[i].Release());

        return Send(target, events.ToArray());
    }

    private static void AddModifier(List<Input> events, ModifierKeys modifiers, ModifierKeys modifier, ushort key)
    {
        if ((modifiers & modifier) != 0 && GetAsyncKeyState(key) >= 0)
            events.Add(Input.Keyboard(key, 0, 0));
    }

    private static bool Send(nint target, Input[] events)
    {
        if (target == 0 || GetForegroundWindow() != target)
            return false;

        int size = Marshal.SizeOf<Input>();
        uint sent = SendInput((uint)events.Length, events, size);
        if (sent == events.Length)
            return true;

        if (sent > 0)
        {
            // A short write must release only keys whose down events reached Windows.
            var release = new List<Input>();
            foreach (Input input in events.Take((int)sent))
            {
                if ((input.Value.Key.Flags & KeyUp) == 0)
                    release.Add(input.Release());
                else
                    release.RemoveAll(key => key.Value.Key.VirtualKey == input.Value.Key.VirtualKey &&
                                             key.Value.Key.Scan == input.Value.Key.Scan);
            }
            release.Reverse();
            if (release.Count > 0)
                SendInput((uint)release.Count, release.ToArray(), size);
        }
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputValue Value;

        public static Input Keyboard(ushort key, ushort scan, uint flags) => new()
        {
            Type = 1,
            Value = new InputValue { Key = new KeyboardInput { VirtualKey = key, Scan = scan, Flags = flags } }
        };

        public readonly Input Release() => Keyboard(Value.Key.VirtualKey, Value.Key.Scan, Value.Key.Flags | KeyUp);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputValue
    {
        [FieldOffset(0)] public KeyboardInput Key;
        [FieldOffset(0)] public MouseInput Mouse; // The largest member determines the native INPUT size.
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint Data, Flags, Time;
        public nuint ExtraInfo;
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char key, nint layout);
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
}
