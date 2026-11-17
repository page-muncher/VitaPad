namespace VitaPad;

/// <summary>Regression checks for controller-driven confirmation and layer routing.</summary>
public static class ExitFlowTests
{
    private const ushort ExitChord = 0x0380;
    private const ushort Cross = 0x1000;
    private const ushort Circle = 0x2000;

    private sealed class Recorder : IOutput
    {
        public readonly List<string> Events = [];
        public void Key(int key, bool down) => Events.Add($"K{key}:{down}");
        public void Mouse(string action, bool down) => Events.Add($"M{action}:{down}");
        public void Move(int x, int y) { if (x != 0 || y != 0) Events.Add($"P{x},{y}"); }
        public void Wheel(int vertical, int horizontal) { if (vertical != 0 || horizontal != 0) Events.Add($"W{vertical},{horizontal}"); }
    }

    public static void Run(Action<bool, string> check)
    {
        static Pad PadWith(ushort buttons) => new() { Buttons = buttons };
        var flow = new ExitFlow();
        flow.Update(PadWith((ushort)(ExitChord | Cross)));
        check(flow.IsOpen && !flow.Confirmed, "Exit chord opens prompt without consuming a held Cross as confirmation");
        flow.Update(PadWith((ushort)(ExitChord | Cross)));
        check(flow.IsOpen && !flow.Confirmed, "Held Cross remains ignored until both face choices are released");
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith((ushort)(ExitChord | Cross)));
        check(flow.Confirmed, "Fresh Cross confirms exit after prompt arms");

        flow = new ExitFlow();
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith((ushort)(ExitChord | Circle)));
        check(!flow.IsOpen && !flow.Confirmed && flow.JustClosed, "Fresh Circle cancels exit instead of confirming");
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith(ExitChord));
        check(!flow.IsOpen && !flow.Confirmed, "Held exit chord cannot reopen a cancelled prompt");
        flow.Update(PadWith(0));
        flow.Update(PadWith(ExitChord));
        check(flow.IsOpen && !flow.Confirmed, "Releasing the chord allows another intentional exit request");

        flow = new ExitFlow();
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith(0));
        flow.Update(PadWith((ushort)(Cross | Circle)));
        check(!flow.IsOpen && !flow.Confirmed, "Simultaneous Cross and Circle safely cancel");

        flow = new ExitFlow();
        flow.Update(PadWith(ExitChord));
        flow.Update(PadWith(0));
        flow.Update(PadWith(Cross));
        check(flow.Confirmed, "Confirmation remains usable after releasing the exit chord");

        flow = new ExitFlow();
        flow.Update(PadWith(0x0080));
        flow.Update(PadWith(ExitChord));
        check(flow.IsOpen, "Exit request works when R3 is held before the bumpers");
        flow.Update(null);
        check(!flow.IsOpen && !flow.Confirmed, "Controller disconnect dismisses the prompt without confirming");
        flow.Update(PadWith(ExitChord));
        check(!flow.IsOpen && !flow.Confirmed, "Reconnect with exit chord held does not reopen the prompt");
        flow.Update(PadWith(0));
        flow.Update(PadWith(ExitChord));
        check(flow.IsOpen, "Neutral after reconnect restores intentional exit requests");
        flow.Cancel();
        flow.Update(PadWith(ExitChord));
        check(!flow.IsOpen && !flow.Confirmed, "External cancel also suppresses a still-held exit chord");

        flow = new ExitFlow();
        flow.Update(PadWith(0x0330));
        check(!flow.IsOpen && !flow.Confirmed, "Old Start + Select exit chord is no longer reserved");

        var output = new Recorder();
        var engine = new Engine(output);
        var profile = new Profile();
        profile.Layers[0].Bindings["A"] = new Binding { Keys = [17, 83], Mouse = ["Left click"] };
        profile.Layers[0].Bindings["B"] = new Binding { Keys = [66] };
        profile.Layers[0].Bindings["R3"] = new Binding { Keys = [82] };
        profile.Layers[1].Bindings["A"] = new Binding { Keys = [65] };
        profile.Layers[2].Bindings["A"] = new Binding { Keys = [67] };
        void Tick(ushort buttons, bool paused = false) => engine.Tick(PadWith(buttons), profile, paused, .008);

        Tick(0);
        Tick(0x0100);
        Tick(0x1100);
        check(output.Events.Contains("K65:True"), "L1 still routes Cross to the L1 layer");
        output.Events.Clear();
        Tick(0x1300);
        check(engine.LayerIndex == 0 && output.Events.Contains("K65:False") && output.Events.Contains("K83:True"), "Both bumpers route Cross to base and release the modifier-layer key");
        output.Events.Clear();
        Tick(0x1380);
        check(engine.Exit.IsOpen && output.Events.Contains("K83:False") && output.Events.Contains("K17:False") && output.Events.Contains("MLeft click:False") && !output.Events.Any(x => x.EndsWith(":True")), "Opening Exit releases keys and mouse and suppresses the reserved R3 output");
        output.Events.Clear();
        Tick(ExitChord);
        Tick((ushort)(ExitChord | Circle));
        check(!engine.Exit.IsOpen && !engine.ExitRequested && !output.Events.Any(x => x.EndsWith(":True")), "Circle cancellation is consumed instead of firing its base binding");
        output.Events.Clear();
        Tick(Circle);
        check(!output.Events.Any(x => x.EndsWith(":True")), "Held cancel button cannot leak output while returning to mappings");
        Tick(0);
        output.Events.Clear();
        Tick(Circle);
        check(output.Events.Contains("K66:True"), "Normal mappings resume after neutral following cancellation");

        Tick(0);
        output.Events.Clear();
        Tick(0x0080);
        check(output.Events.Contains("K82:True"), "R3 remains assignable without both bumpers");
        output.Events.Clear();
        Tick(ExitChord);
        check(engine.Exit.IsOpen && output.Events.Contains("K82:False"), "Joining bumpers while R3 is held releases the prior R3 mapping");

        output = new Recorder();
        engine = new Engine(output);
        Tick(0, true);
        Tick(ExitChord, true);
        check(engine.Exit.IsOpen && output.Events.Count == 0, "Exit prompt works with editor mappings paused");
        Tick(0, true);
        Tick(Cross, true);
        check(engine.ExitRequested && output.Events.Count == 0, "Cross confirms exit while the editor remains paused");

        output = new Recorder();
        engine = new Engine(output);
        Tick(0);
        Tick(0x0330);
        check(!engine.Exit.IsOpen && !engine.ExitRequested && engine.LayerIndex == 0, "Old four-button chord now stays on base without exiting");
    }
}
