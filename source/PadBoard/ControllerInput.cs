using System.Runtime.InteropServices;

namespace ControllerKeyboard;

[Flags]
internal enum PadButtons : ushort
{
    Up = 0x0001,
    Down = 0x0002,
    Left = 0x0004,
    Right = 0x0008,
    DPad = Up | Down | Left | Right,
    LeftStick = 0x0040,
    RightStick = 0x0080,
    Layout = 0x0100,
    CaretMode = 0x0200,
    Cross = 0x1000,
    Circle = 0x2000,
    Square = 0x4000,
    Triangle = 0x8000
}

internal readonly record struct PadState(PadButtons Buttons, byte LeftTrigger = 0, byte RightTrigger = 0,
    short RightX = 0, short RightY = 0, short LeftX = 0, short LeftY = 0);

internal enum OverlayCommand { None, Reset, Toggle, Quit }

internal readonly record struct PadFrame(
    bool CircleHeld, bool CaretMode, bool SelectText, bool SwapLayout, bool Paste, bool Cross, bool CrossRepeat, bool Triangle, bool Exit,
    bool Backspace, bool JumpLeft, bool JumpRight, PadButtons Direction, OverlayCommand Overlay, bool CrossHeld,
    double MoveX, double MoveY, PadButtons Adjustment, bool CircleTap, double Timestamp);

internal sealed class ControllerInput
{
    private const byte TriggerPress = 42;
    private const byte TriggerRelease = 25;
    private const int RepeatDelay = 310;
    private const int RepeatInterval = 75;
    private const int StickChordWindow = 85;
    private const int CircleHoldMilliseconds = 300;
    private const PadButtons StickClicks = PadButtons.LeftStick | PadButtons.RightStick;

    private PadButtons previous;
    private PadButtons direction;
    private bool leftHeld;
    private bool rightHeld;
    private bool synchronized;
    private bool circleLayer;
    private double circleStarted;
    private double nextMove;
    private double nextBackspace;
    private double nextCross;
    private double previousUpdate;
    private bool leftStickReady;
    private bool blockStickClicks;
    private PadButtons pendingStick;
    private double stickDeadline;
    private double velocityX;
    private double velocityY;

    public void Reset() => synchronized = false;

    public PadFrame Update(PadState state, double now, bool keyboardShown)
    {
        bool caretMode = (state.Buttons & PadButtons.CaretMode) != 0;
        if (!synchronized)
        {
            previous = state.Buttons;
            direction = PickDirection(previous & PadButtons.DPad);
            leftHeld = state.LeftTrigger > TriggerRelease;
            rightHeld = state.RightTrigger > TriggerRelease;
            nextMove = double.PositiveInfinity;
            nextBackspace = double.PositiveInfinity;
            nextCross = double.PositiveInfinity;
            circleLayer = false;
            previousUpdate = now;
            leftStickReady = Math.Max(Math.Abs((int)state.LeftX), Math.Abs((int)state.LeftY)) <= 11000;
            blockStickClicks = (state.Buttons & StickClicks) != 0;
            pendingStick = 0;
            velocityX = velocityY = 0;
            synchronized = true;
            return new PadFrame { CaretMode = caretMode };
        }

        PadButtons pressed = state.Buttons & ~previous;
        PadButtons released = previous & ~state.Buttons;
        previous = state.Buttons;

        PadButtons dpad = state.Buttons & PadButtons.DPad;
        PadButtons nextDirection = PickDirection(pressed & PadButtons.DPad);
        if (nextDirection == 0)
            nextDirection = (dpad & direction) != 0 ? direction : PickDirection(dpad);

        bool moved = nextDirection != 0 && Repeat(nextDirection != direction, now, ref nextMove);
        direction = nextDirection;

        bool circlePressed = (pressed & PadButtons.Circle) != 0;
        bool circleReleased = (released & PadButtons.Circle) != 0;
        bool circleTap = circleReleased && circleLayer && !caretMode &&
            now - circleStarted < CircleHoldMilliseconds;
        if (circlePressed)
        {
            circleLayer = !caretMode;
            circleStarted = now;
        }
        if (caretMode || circleReleased)
            circleLayer = false;

        bool squarePressed = (pressed & PadButtons.Square) != 0;
        bool squareHeld = (state.Buttons & PadButtons.Square) != 0;
        bool l1Held = (state.Buttons & PadButtons.Layout) != 0;
        bool selectText = caretMode && l1Held;
        bool backspace = caretMode ? squarePressed :
            squareHeld && Repeat(squarePressed, now, ref nextBackspace);
        // Entering caret mode stops an already repeating regular Backspace.
        if (caretMode)
            nextBackspace = double.PositiveInfinity;
        bool l1Pressed = (pressed & PadButtons.Layout) != 0;
        bool crossPressed = (pressed & PadButtons.Cross) != 0;
        bool crossHeld = (state.Buttons & PadButtons.Cross) != 0;
        bool crossRepeat = crossHeld && Repeat(crossPressed, now, ref nextCross) && !crossPressed;
        (double moveX, double moveY) = keyboardShown
            ? StickMovement(state.RightX, state.RightY, Math.Clamp(now - previousUpdate, 0, 50))
            : (0, 0);
        previousUpdate = now;

        return new PadFrame(
            circleLayer && (state.Buttons & PadButtons.Circle) != 0,
            caretMode,
            selectText,
            l1Pressed && !caretMode,
            caretMode && l1Held && (pressed & PadButtons.Triangle) != 0,
            crossPressed,
            crossRepeat,
            (pressed & PadButtons.Triangle) != 0,
            caretMode && (circlePressed || ((pressed & PadButtons.CaretMode) != 0 && (state.Buttons & PadButtons.Circle) != 0)),
            backspace,
            TriggerPressed(state.LeftTrigger, ref leftHeld),
            TriggerPressed(state.RightTrigger, ref rightHeld),
            moved ? direction : 0,
            DecodeStickClicks(state.Buttons, pressed, now, keyboardShown),
            crossHeld,
            moveX,
            moveY,
            DecodeAdjustment(state.LeftX, state.LeftY),
            circleTap,
            now);
    }

    private OverlayCommand DecodeStickClicks(PadButtons buttons, PadButtons pressed, double now, bool keyboardShown)
    {
        PadButtons held = buttons & StickClicks;
        PadButtons fresh = pressed & StickClicks;
        if (blockStickClicks)
        {
            blockStickClicks = held != 0;
            return OverlayCommand.None;
        }

        if (held == StickClicks && fresh != 0)
        {
            pendingStick = 0;
            return keyboardShown ? OverlayCommand.Quit : OverlayCommand.None;
        }

        if (!keyboardShown)
            return SingleStickCommand(fresh);

        // Delay a lone click just long enough to catch the other stick arriving a few polls later.
        if (fresh != 0)
        {
            pendingStick = fresh;
            stickDeadline = now + StickChordWindow;
        }

        if (pendingStick == 0 || ((held & pendingStick) != 0 && now < stickDeadline))
            return OverlayCommand.None;

        OverlayCommand command = SingleStickCommand(pendingStick);
        pendingStick = 0;
        return command;
    }

    private static OverlayCommand SingleStickCommand(PadButtons button) => button switch
    {
        PadButtons.LeftStick => OverlayCommand.Reset,
        PadButtons.RightStick => OverlayCommand.Toggle,
        _ => OverlayCommand.None
    };

    private PadButtons DecodeAdjustment(short x, short y)
    {
        int horizontal = Math.Abs((int)x);
        int vertical = Math.Abs((int)y);
        int strength = Math.Max(horizontal, vertical);
        if (strength <= 11000)
            leftStickReady = true;
        if (!leftStickReady || strength < 19500)
            return 0;

        leftStickReady = false;
        if (horizontal > vertical)
            return x < 0 ? PadButtons.Left : PadButtons.Right;
        return y < 0 ? PadButtons.Down : PadButtons.Up;
    }

    private (double X, double Y) StickMovement(short rawX, short rawY, double milliseconds)
    {
        const double deadZone = 0.25;
        const double maxSpeed = 920;
        double x = rawX / 32768d;
        double y = -rawY / 32768d;
        double length = Math.Sqrt(x * x + y * y);
        if (length <= deadZone)
        {
            velocityX = velocityY = 0;
            return (0, 0);
        }

        double tilt = (Math.Min(length, 1) - deadZone) / (1 - deadZone);
        double speed = maxSpeed * tilt * (0.28 + 0.72 * tilt);
        double blend = 1 - Math.Exp(-milliseconds / 20);
        velocityX += (x / length * speed - velocityX) * blend;
        velocityY += (y / length * speed - velocityY) * blend;
        return (velocityX * milliseconds / 1000, velocityY * milliseconds / 1000);
    }

    private static PadButtons PickDirection(PadButtons buttons)
    {
        if ((buttons & PadButtons.Up) != 0) return PadButtons.Up;
        if ((buttons & PadButtons.Down) != 0) return PadButtons.Down;
        if ((buttons & PadButtons.Left) != 0) return PadButtons.Left;
        return buttons & PadButtons.Right;
    }

    private static bool Repeat(bool pressed, double now, ref double next)
    {
        if (!pressed && now < next)
            return false;

        next = now + (pressed ? RepeatDelay : RepeatInterval);
        return true;
    }

    private static bool TriggerPressed(byte value, ref bool held)
    {
        bool pressed = !held && value >= TriggerPress;
        held = held ? value > TriggerRelease : pressed;
        return pressed;
    }
}

internal sealed class XInputReader
{
    private uint? activeSlot;
    private long nextScan;

    public bool TryRead(long now, out PadState pad, out bool newController)
    {
        pad = default;
        newController = false;

        if (activeSlot is uint slot && Read(slot, out pad))
            return true;

        activeSlot = null;
        if (now < nextScan)
            return false;

        nextScan = now + 300;
        for (uint index = 0; index < 4; index++)
        {
            if (!Read(index, out pad))
                continue;

            activeSlot = index;
            newController = true;
            return true;
        }

        return false;
    }

    private static bool Read(uint slot, out PadState pad)
    {
        bool connected = XInputGetState(slot, out NativeState state) == 0;
        pad = new PadState(state.Buttons, state.LeftTrigger, state.RightTrigger, state.RightX, state.RightY, state.LeftX, state.LeftY);
        return connected;
    }

    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern uint XInputGetState(uint userIndex, out NativeState state);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeState
    {
        public uint Packet;
        public PadButtons Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftX;
        public short LeftY;
        public short RightX;
        public short RightY;
    }
}
