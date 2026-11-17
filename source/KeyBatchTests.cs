using System.Runtime.InteropServices;

namespace VitaPad;

public static class KeyBatchTests
{
    public static void Run(Action<bool, string> check)
    {
        var batches = new List<(uint Count, NativeOutput.Input[] Events, int ByteSize)>();
        bool partial = false;
        var output = new NativeOutput((count, events, byteSize) =>
        {
            batches.Add((count, events, byteSize));
            return partial ? count - 1 : count;
        });
        var profile = new Profile();
        profile.Layers[1].Bindings["R2"] = new() { Keys = [76, 18] }; // Alt+L, deliberately entered in reverse.
        profile.Layers[1].Bindings["X"] = new() { Keys = [91, 68] }; // Left Win+D.
        var engine = new Engine(output);

        void Tick(ushort buttons = 0, byte rt = 0) => engine.Tick(new Pad { Buttons = buttons, RT = rt }, profile, false, .008);
        bool IsBatch(params (ushort Key, uint Flags)[] events) =>
            batches.Count == 1 && batches[0].Count == events.Length &&
            batches[0].Events.All(i => i.type == 1) &&
            batches[0].Events.Select(i => (i.value.key.vk, i.value.key.flags)).SequenceEqual(events);

        int expectedSize = IntPtr.Size == 8 ? 40 : 28;
        int expectedUnionOffset = IntPtr.Size == 8 ? 8 : 4;
        int expectedKeyExtraOffset = IntPtr.Size == 8 ? 16 : 12;
        check(Marshal.SizeOf<NativeOutput.Input>() == expectedSize &&
            Marshal.OffsetOf<NativeOutput.Input>(nameof(NativeOutput.Input.value)).ToInt32() == expectedUnionOffset &&
            Marshal.OffsetOf<NativeOutput.KeyInput>(nameof(NativeOutput.KeyInput.extra)).ToInt32() == expectedKeyExtraOffset,
            "SendInput packet matches Windows INPUT and KEYBDINPUT layout");

        Tick();
        Tick(0x0100, 255);
        check(IsBatch((18, 0), (76, 0)) && batches[0].ByteSize == expectedSize && !output.Failed,
            "L1+R2 sends generic Alt+L together, modifiers first, with full SendInput count");

        batches.Clear();
        Tick(0x0100); // Keep the layer modifier held; release just the trigger.
        check(IsBatch((76, 2), (18, 2)) && engine.LayerIndex == 1,
            "Releasing R2 while L1 stays held sends L-up and Alt-up in one SendInput call");
        batches.Clear();
        Tick(0x0100);
        check(batches.Count == 0, "Alt does not remain held or get released again after the chord ends");

        profile.Layers[1].Bindings["B"] = new() { Keys = [18] };
        Tick(0x0100, 255);
        batches.Clear();
        Tick(0x2100, 255);
        check(batches.Count == 0, "An additional owner of held Alt does not send a duplicate keydown");
        Tick(0x2100);
        check(IsBatch((76, 2)), "Releasing the chord preserves Alt when another binding still owns it");
        batches.Clear();
        Tick(0x0100);
        check(IsBatch((18, 2)), "The last Alt owner sends the remaining keyup");

        batches.Clear();
        Tick(0x4100);
        check(IsBatch((91, 1), (68, 0)), "Win chord retains the extended Windows keydown in the press batch");
        batches.Clear();
        Tick(0x0100);
        check(IsBatch((68, 2), (91, 3)), "Win chord retains the extended Windows keyup in the release batch");

        profile.Layers[1].Bindings["A"] = new() { Keys = [76] };
        Tick(0x1100); // L first, then a modifier owned by another button.
        Tick(0x3100);
        batches.Clear();
        Tick(0x0100);
        check(IsBatch((76, 2), (18, 2)), "Simultaneous releases send ordinary keys first even when Alt was acquired later");

        Tick(0x0100, 255);
        batches.Clear();
        engine.Release();
        check(IsBatch((76, 2), (18, 2)), "Explicit Release sends all held keys in one ordered batch");
        Tick(0x0100, 255);
        batches.Clear();
        engine.Reset();
        check(IsBatch((76, 2), (18, 2)), "Reset uses the same ordered release batch");
        batches.Clear();
        engine.Reset();
        output.Keys([], false);
        check(batches.Count == 0, "Empty releases do not call SendInput");

        Tick(0x0100);
        Tick(0x0100, 255);
        batches.Clear();
        partial = true;
        Tick(0x0100);
        check(IsBatch((76, 2), (18, 2)) && output.Failed, "Partial SendInput insertion is reported as failure");
    }
}
