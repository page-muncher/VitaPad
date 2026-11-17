using ControllerKeyboard;

namespace VitaPad;

public sealed partial class MainWindow
{
    private readonly PadBoardHandoff handoff = new();
    private ControllerKeyboard.MainWindow? padBoard;
    private DesktopPollClock? controllerClock;
    private bool padBoardOpen => padBoard?.KeyboardVisible == true;
    private Pad? referenceState;
    private Pad lastPad;

    private void RouteController(Pad? pad, double dt)
    {
        referenceState = null;
        if (pad is not Pad raw || IsVisible || paused)
        {
            StopPadBoard();
            handoff.Capture(default, Current);
            engine.Tick(pad, Current, true, dt);
            return;
        }
        lastPad = raw;

        Pad filtered = handoff.Filter(raw, Current);
        referenceState = filtered;
        bool wasOpen = padBoardOpen;
        // VitaPad retains its exit chord while PadBoard owns the other controls.
        engine.Tick(wasOpen ? raw : filtered, Current, wasOpen, dt);
        if (engine.Exit.IsOpen || engine.ExitRequested || engine.Exit.JustClosed)
        {
            StopPadBoard();
            return;
        }

        if (wasOpen)
        {
            padBoard!.AcceptControllerState(new ControllerKeyboard.PadState(
                (PadButtons)filtered.Buttons, filtered.LT, filtered.RT,
                filtered.RX, filtered.RY, filtered.LX, filtered.LY));
            if (!padBoardOpen)
            {
                referenceState = null;
                handoff.Capture(raw, Current);
                controllerClock?.SetVisible(false);
                engine.ResumeAfterPadBoard(Current);
            }
        }
        else if (engine.PadBoardRequested)
        {
            handoff.Capture(raw, Current);
            hint.Reset();
            overlay.Hide();
            if (padBoard is null)
            {
                padBoard = new ControllerKeyboard.MainWindow(desktop: true);
                padBoard.QuitRequested += (_, _) => Exit();
                padBoard.KeyboardClosed += () =>
                {
                    handoff.Capture(lastPad, Current);
                    controllerClock?.SetVisible(false);
                    engine.ResumeAfterPadBoard(Current);
                };
            }
            padBoard.OpenFromMapper();
            controllerClock?.SetVisible(padBoardOpen);
            engine.ResumeAfterPadBoard(Current);
        }
    }

    private void StopPadBoard()
    {
        if (!padBoardOpen)
            return;

        padBoard!.CloseFromMapper();
        controllerClock?.SetVisible(false);
    }
}
