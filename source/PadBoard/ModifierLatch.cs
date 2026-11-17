namespace ControllerKeyboard;

internal sealed class ModifierLatch
{
    internal const double DoubleTapMilliseconds = 300;
    private const double CancelMilliseconds = 1000;

    private bool armed;
    private bool locked;
    private double armedAt;

    public bool Active => armed || locked;
    public bool Locked => locked;

    public void Tap(double now)
    {
        if (locked)
        {
            locked = false;
            return;
        }

        if (armed && now - armedAt is >= 0 and <= DoubleTapMilliseconds)
        {
            armed = false;
            locked = true;
            return;
        }

        if (armed && now - armedAt >= CancelMilliseconds)
        {
            armed = false;
            return;
        }

        armed = true;
        armedAt = now;
    }

    public bool Consume()
    {
        bool changed = armed;
        armed = false;
        return changed;
    }

    public void Reset()
    {
        armed = false;
        locked = false;
    }
}
