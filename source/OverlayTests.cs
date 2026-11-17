namespace VitaPad;

public static class OverlayTests
{
    public static void Run(Action<bool, string> check)
    {
        var profile = new Profile();
        check(LayerOverlayWindow.GetEntries(profile, 0, false).Count == 0, "Overlay omits unassigned controls");
        foreach (var id in Engine.Ids) profile.Layers[0].Bindings[id] = new Binding { Keys = [65] };
        var all = LayerOverlayWindow.GetEntries(profile, 0, false);
        check(all.Count == 22 && all.Select(e => e.Id).Distinct().Count() == 22, "Overlay includes all 22 assigned controls");
        check(all.Any(e => e.Id == "Start") && all.Any(e => e.Id == "Select") && all.Any(e => e.Id == "L2") && all.Any(e => e.Id == "R2") && all.Count(e => e.Id.Contains("stick ")) == 8, "Overlay covers triggers, menu controls, and stick directions");
        var both = LayerOverlayWindow.GetEntries(profile, 0, true);
        check(both.Count == 21 && both.All(e => e.Id != "R3") && both.Any(e => e.Id == "L3"), "Both-bumper overlay reserves R3 without hiding other bindings");
        profile.Layers[0].LeftStick = "Pointer";
        profile.Layers[0].RightStick = "Scroll";
        var modes = LayerOverlayWindow.GetEntries(profile, 0, false);
        check(modes.Any(e => e.Id == "Left stick" && e.Value == "Mouse pointer") && modes.Any(e => e.Id == "Right stick" && e.Value == "Mouse scroll") && modes.All(e => !e.Id.StartsWith("Left stick ") && !e.Id.StartsWith("Right stick ")), "Overlay shows active stick modes instead of dormant directional bindings");
        profile.Layers[1].Bindings["Y"] = new Binding { Keys = [17, 83] };
        var l1 = LayerOverlayWindow.GetEntries(profile, 1, false);
        check(l1.Count == 1 && l1[0].Id == "Y" && l1[0].Value == "Ctrl + S", "Overlay resolves the selected modifier layer");
        check(LayerOverlayWindow.ControlName("A") == "× Cross" && LayerOverlayWindow.ControlName("B") == "○ Circle" && LayerOverlayWindow.ControlName("X") == "□ Square" && LayerOverlayWindow.ControlName("Y") == "△ Triangle", "Overlay face symbols preserve the standard Xbox input identities");
        profile.Layers[1].Bindings["A"] = new Binding();
        check(LayerOverlayWindow.GetEntries(profile, 1, false).Count == 1, "Overlay hides explicitly cleared bindings");
    }
}
