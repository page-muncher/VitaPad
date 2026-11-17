namespace VitaPad;

// Wait for each held control to release before the new mode can use it.
internal sealed class PadBoardHandoff
{
    private ushort buttons;
    private bool leftTrigger, rightTrigger, leftStick, rightStick;

    public void Capture(Pad pad, Profile profile)
    {
        buttons = pad.Buttons;
        leftTrigger = pad.LT > TriggerNeutral(profile);
        rightTrigger = pad.RT > TriggerNeutral(profile);
        leftStick = !StickNeutral(pad.LX, pad.LY, profile);
        rightStick = !StickNeutral(pad.RX, pad.RY, profile);
    }

    public Pad Filter(Pad pad, Profile profile)
    {
        buttons &= pad.Buttons;
        pad.Buttons &= (ushort)~buttons;

        leftTrigger &= pad.LT > TriggerNeutral(profile);
        rightTrigger &= pad.RT > TriggerNeutral(profile);
        if (leftTrigger) pad.LT = 0;
        if (rightTrigger) pad.RT = 0;

        leftStick &= !StickNeutral(pad.LX, pad.LY, profile);
        rightStick &= !StickNeutral(pad.RX, pad.RY, profile);
        if (leftStick) pad.LX = pad.LY = 0;
        if (rightStick) pad.RX = pad.RY = 0;
        return pad;
    }

    private static double TriggerNeutral(Profile profile) => Math.Min(25, profile.TriggerThreshold * 255 - 1);

    private static bool StickNeutral(short x, short y, Profile profile) =>
        Math.Max(Math.Abs((int)x), Math.Abs((int)y)) <= profile.DeadZone * 32768 &&
        (double)x * x + (double)y * y <= 8192 * 8192;
}
