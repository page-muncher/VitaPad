namespace VitaPad;

// A hint is one opportunity per bumper hold. Using a binding consumes it until
// both bumpers are released, even if the user switches between bumper layers.
internal sealed class HintGate
{
    const double Delay = .5;
    const ushort Bumpers = 0x300;
    ushort pattern;
    double quietSince;
    bool consumed;

    public void Reset()
    {
        pattern = 0;
        consumed = false;
    }

    public bool Update(ushort buttons, bool usedBinding, double now)
    {
        var next = (ushort)(buttons & Bumpers);
        if (next == 0)
        {
            Reset();
            return false;
        }
        if (pattern == 0) quietSince = now;
        pattern = next;
        consumed |= usedBinding;
        return !consumed && now >= quietSince + Delay;
    }

    public static bool HasActiveBinding(Layer layer, IReadOnlySet<string> active)
    {
        foreach (var id in active)
        {
            if ((layer.LeftStick != "Keys" && id.StartsWith("Left stick ", StringComparison.Ordinal)) ||
                (layer.RightStick != "Keys" && id.StartsWith("Right stick ", StringComparison.Ordinal))) return true;
            if (layer.Bindings.TryGetValue(id, out var binding) &&
                (binding.PadBoard || binding.Keys.Count != 0 || binding.Mouse.Count != 0)) return true;
        }
        return false;
    }
}

internal static class HintGateTests
{
    public static void Run(Action<bool,string> check)
    {
        const ushort left=0x100,right=0x200,both=0x300;
        var gate=new HintGate();
        check(!gate.Update(left,false,1) && !gate.Update(left,false,1.499), "Hint waits half a second");
        check(gate.Update(left,false,1.5), "Quiet bumper hold displays hint at the deadline");
        check(!gate.Update(left,true,1.51), "Assigned control immediately hides visible hint");
        check(!gate.Update(left,false,3), "Releasing the assigned control alone does not restore hint");
        check(!gate.Update(both,false,4) && !gate.Update(right,false,5) && !gate.Update(right,false,6), "Switching bumpers without releasing both does not restore hint");
        gate.Update(0,false,7);
        check(!gate.Update(right,false,8) && gate.Update(right,false,8.5), "Release and re-hold re-arms hint");
        gate.Update(0,false,9);
        gate.Update(left,false,10);
        gate.Update(left,true,10.1);
        check(!gate.Update(left,false,11), "Quick hotkey before deadline also consumes hint");
        gate.Reset();
        gate.Update(left,false,12);
        check(!gate.Update(both,false,12.45) && gate.Update(both,false,12.5), "Adding the second bumper preserves elapsed preview delay");
        check(gate.Update(right,false,12.51) && gate.Update(both,false,12.52) && gate.Update(left,false,12.53), "A visible preview stays shown across bumper combinations");
        gate.Reset();
        gate.Update(right,false,13);
        check(!gate.Update(left,false,13.3) && gate.Update(left,false,13.5), "Switching directly between bumpers preserves the countdown");

        var layer = new Layer();
        var active = new HashSet<string>{"A", "Left stick Up"};
        layer.Bindings["A"] = new Binding();
        check(!HintGate.HasActiveBinding(layer,active), "Unassigned and cleared controls do not hide the hint");
        layer.Bindings["A"].Keys.Add(8);
        check(HintGate.HasActiveBinding(layer,active), "Assigned Backspace hides the hint");
        layer.Bindings.Clear();
        layer.LeftStick="Pointer";
        check(HintGate.HasActiveBinding(layer,active), "Assigned analog stick motion hides the hint");
        layer.LeftStick="Keys";
        layer.Bindings["Left stick Up"]=new Binding {Mouse=["Wheel Up"]};
        check(HintGate.HasActiveBinding(layer,active), "Assigned mouse and wheel outputs hide the hint");
    }
}
