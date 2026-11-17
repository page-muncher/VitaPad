namespace VitaPad;

// Controller-driven confirmation stays independent of the editor and never sends keys.
public sealed class ExitFlow
{
    const ushort Chord = 0x380; // L1 + R1 + R3
    bool chordLatched;
    bool faceReady;
    ushort previousFaces;
    public bool IsOpen { get; private set; }
    public bool Confirmed { get; private set; }
    public bool JustClosed { get; private set; }

    public void Update(Pad? state)
    {
        JustClosed = false;
        if (state is not Pad pad)
        {
            JustClosed = IsOpen;
            IsOpen = false;
            chordLatched = true;
            faceReady = false;
            previousFaces = 0;
            return;
        }
        ushort faces = (ushort)(pad.Buttons & 0x3000);
        bool chord = (pad.Buttons & Chord) == Chord;
        if (!chord) chordLatched = false;
        if (Confirmed) return;
        if (IsOpen)
        {
            if (faces == 0) faceReady = true;
            ushort pressed = (ushort)(faces & ~previousFaces);
            if (faceReady && (pressed & 0x2000) != 0) Cancel(); // Circle wins ties.
            else if (faceReady && (pressed & 0x1000) != 0)
            {
                Confirmed = true;
                IsOpen = false;
                JustClosed = true;
            }
        }
        else if (chord && !chordLatched)
        {
            IsOpen = true;
            chordLatched = true;
            faceReady = faces == 0;
        }
        previousFaces = faces;
    }

    public void Cancel()
    {
        JustClosed = IsOpen;
        IsOpen = false;
        chordLatched = true;
        faceReady = false;
    }
}
