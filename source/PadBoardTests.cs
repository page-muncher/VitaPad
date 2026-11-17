using System.Text.Json;

namespace VitaPad;

internal static class PadBoardTests
{
    internal sealed class Recorder : IOutput
    {
        public List<string> Events { get; } = [];
        public void Key(int key, bool down) => Events.Add($"K{key}:{down}");
        public void Mouse(string action, bool down) => Events.Add($"M{action}:{down}");
        public void Move(int x, int y) { }
        public void Wheel(int vertical, int horizontal) { }
    }

    public static void Run(Action<bool, string> check)
    {
        const ushort r3 = 0x80, l1 = 0x100, cross = 0x1000;
        var settings = new Settings();
        var profile = settings.Profiles[0];
        profile.Layers[0].Bindings["R3"] = new Binding { PadBoard = true, Keys = [90] };
        profile.Layers[0].Bindings["A"] = new Binding { Keys = [18, 76], Mouse = ["Left click"] };
        profile.Layers[1].Bindings["R3"] = new Binding { Keys = [66] };
        var roundTrip = Settings.Parse(JsonSerializer.Serialize(settings, Settings.Json));
        var binding = roundTrip.Profiles[0].Layers[0].Bindings["R3"];
        check(binding.PadBoard && binding.Keys.SequenceEqual(new[] { 90 }) && binding.Label == "PadBoard",
            "Settings preserve the PadBoard selection and underlying key assignments");
        check(!Settings.Parse("{}").Profiles[0].Layers[0].Bindings.ContainsKey("R3"),
            "Existing settings without PadBoard stay compatible");

        var recorder = new Recorder();
        var engine = new Engine(recorder);
        void Tick(ushort buttons = 0, bool paused = false) => engine.Tick(new Pad { Buttons = buttons }, profile, paused, .008);
        Tick(r3);
        check(!engine.PadBoardRequested, "Held R3 on connection does not open PadBoard");
        Tick();
        Tick(r3, paused: true);
        check(!engine.PadBoardRequested, "Paused mappings cannot open PadBoard");
        Tick();
        Tick(cross);
        recorder.Events.Clear();
        Tick(r3 | cross);
        check(engine.PadBoardRequested && recorder.Events.SequenceEqual(new[] { "K76:False", "K18:False", "MLeft click:False" }),
            "Opening PadBoard releases held keys and mouse without firing R3's stored key");
        Tick(r3 | cross);
        check(!engine.PadBoardRequested, "Holding R3 cannot reopen PadBoard");
        Tick();
        recorder.Events.Clear();
        Tick(l1 | r3);
        check(!engine.PadBoardRequested && recorder.Events.Contains("K66:True"), "L1 R3 keeps its keyboard binding");
        Tick(r3);
        check(!engine.PadBoardRequested && recorder.Events.Contains("K66:False"), "Releasing L1 while R3 is held does not enter PadBoard");
        Tick();
        Tick(0x380);
        check(engine.Exit.IsOpen && !engine.PadBoardRequested, "VitaPad exit chord takes priority over PadBoard");

        var gate = new PadBoardHandoff();
        var held = new Pad { Buttons = r3, RT = 255, LX = 30000, RY = -30000 };
        gate.Capture(held, profile);
        held.Buttons |= cross;
        var filtered = gate.Filter(held, profile);
        check(filtered.Buttons == cross && filtered.RT == 0 && filtered.LX == 0 && filtered.RY == 0,
            "The handoff filters held controls and allows a fresh button immediately");
        gate.Filter(default, profile);
        filtered = gate.Filter(held, profile);
        check(filtered.Buttons == held.Buttons && filtered.RT == held.RT && filtered.LX == held.LX && filtered.RY == held.RY,
            "Triggers, sticks and buttons rearm independently after release");
    }
}
