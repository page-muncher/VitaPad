using System.Windows.Automation;
using System.Windows.Input;

namespace ControllerKeyboard;

internal static class ModifierTests
{
    internal static void Run(Action<bool, string> check)
    {
        var latch = new ModifierLatch();
        latch.Tap(10);
        check(latch.Active && latch.Consume() && !latch.Active, "Modifier arms for one emitted key");
        latch.Tap(100);
        latch.Tap(400);
        check(latch.Active && !latch.Consume(), "Second modifier tap at 300 ms locks through output");
        latch.Tap(1000);
        check(!latch.Active, "Tapping a locked modifier unlocks it");
        latch.Tap(2000);
        latch.Tap(2301);
        check(latch.Consume() && !latch.Active, "Late second tap rearms without locking");
        latch.Tap(4000);
        latch.Consume();
        latch.Tap(4200);
        check(latch.Consume(), "A tap after consumption starts a new one-shot");
        latch.Tap(5000);
        latch.Tap(5999);
        check(latch.Active && latch.Consume(), "A tap just before one second keeps the modifier armed");
        latch.Tap(7000);
        latch.Tap(8000);
        check(!latch.Active, "A tap after one second cancels an armed modifier");

        var input = new ControllerInput();
        input.Update(default, 0, false);
        check(input.Update(new(PadButtons.Circle), 15, false).CircleHeld &&
            input.Update(default, 40, false).CircleTap, "Circle applies immediately while down and reports a short tap on release");
        input.Update(new(PadButtons.Circle), 100, false);
        check(!input.Update(default, 400, false).CircleTap, "A long Circle hold releases without arming a one-shot");
        var close = input.Update(new(PadButtons.Circle | PadButtons.CaretMode), 500, false);
        check(close.Exit && !close.CircleHeld && !input.Update(default, 520, false).CircleTap,
            "R1 plus Circle closes without registering a modifier tap");
        input.Update(new(PadButtons.CaretMode), 600, false);
        check(input.Update(new(PadButtons.CaretMode | PadButtons.Square), 620, false).Backspace &&
            !input.Update(new(PadButtons.CaretMode | PadButtons.Square), 1000, false).Backspace,
            "R1 Square activates word deletion once per press");
        input.Update(new(PadButtons.CaretMode), 1100, false);
        var selection = input.Update(new(PadButtons.CaretMode | PadButtons.Layout | PadButtons.Left), 1120, false);
        check(selection.SelectText && selection.Direction == PadButtons.Left && !selection.Paste && !selection.SwapLayout,
            "R1 L1 D-pad selects without pasting or changing layouts");
        var paste = input.Update(new(PadButtons.CaretMode | PadButtons.Layout | PadButtons.Triangle), 1140, false);
        check(paste.Paste && paste.Triangle, "R1 L1 Triangle requests Paste");
    }
}

public partial class MainWindow
{
    internal static void CheckModifiers(Action<bool, string> check)
    {
        var keyboard = new MainWindow();
        try
        {
            var decoder = new ControllerInput();
            double now = 0;
            void Step(PadButtons buttons = 0, int ms = 16) =>
                keyboard.ApplyPadFrame(decoder.Update(new(buttons), now += ms, false));
            void Tap(PadButtons buttons) { Step(buttons); Step(); }
            void Key(int row, int col) { keyboard.Highlight(row, col); Tap(PadButtons.Cross); }
            bool HasLabel(string text) => keyboard.page.Buttons.Values.Any(b => AutomationProperties.GetName(b) == text);
            PadButtons circle = PadButtons.Circle, cross = PadButtons.Cross, layout = PadButtons.Layout;

            Step();
            Tap(circle);
            Tap(PadButtons.Right);
            check(keyboard.ShiftActive && HasLabel("F"), "Circle arms QWERTY Shift through highlight navigation");
            Tap(cross);
            Tap(cross);
            check(keyboard.Editor.Text == "Ff" && !keyboard.ShiftActive, "QWERTY Circle shifts only the next emitted letter");
            Tap(circle);
            Tap(circle);
            Tap(cross);
            Tap(cross);
            check(keyboard.Editor.Text == "FfFF" && keyboard.ShiftActive, "Double Circle tap locks Shift across emitted keys");
            Tap(circle);
            check(!keyboard.ShiftActive, "A later Circle tap unlocks Shift");
            Step(circle);
            Step(circle | cross);
            Step(circle);
            Step();
            check(keyboard.Editor.Text == "FfFFF" && !keyboard.ShiftActive, "Circle held while typing does not rearm Shift on release");
            Tap(circle);
            Tap(PadButtons.Triangle);
            check(!keyboard.ShiftActive, "Space consumes one-shot Shift");
            Tap(circle);
            Tap(PadButtons.CaretMode | PadButtons.Left);
            check(!keyboard.ShiftActive, "A caret arrow consumes one-shot Shift");

            Tap(layout);
            check(KeyboardLayouts.Qwerty.Keys[3, 1].Label == "Alt" && HasLabel("Shift") &&
                  KeyboardLayouts.Symbols.Keys[3, 1].Label == "Alt", "Alt sits at the same location on both layouts");
            Step(circle);
            check(HasLabel("F1") && !keyboard.ShiftActive, "Second layout reveals alternates on initial Circle press");
            Step();
            check(HasLabel("F1"), "A Circle tap keeps alternates armed through release");
            Key(0, 0);
            check(!HasLabel("F1"), "A function key consumes the one-shot alternate layer");
            Tap(circle);
            Tap(circle);
            Key(0, 0);
            check(HasLabel("F1"), "The alternate layer remains active after a double tap and function key");
            Tap(circle);
            check(!HasLabel("F1"), "A later Circle tap unlocks the alternate layer");
            Step(circle);
            Step(circle, 310);
            check(HasLabel("F1"), "Holding Circle keeps the alternate layer visible");
            Step();
            check(!HasLabel("F1"), "Long Circle hold release restores the regular layer");

            Key(3, 0);
            Key(3, 2);
            check(keyboard.ControlActive && keyboard.ShiftActive, "On-screen Ctrl and Shift arm simultaneously");
            Tap(layout);
            check(keyboard.ControlActive && keyboard.ShiftActive, "Changing layouts preserves armed modifiers");
            Tap(circle);
            Tap(PadButtons.Square);
            check(!keyboard.ControlActive && keyboard.ShiftActive, "Circle shares Shift state and Backspace consumes only armed modifiers");
            Tap(circle);
            Key(3, 1);
            check(keyboard.AltActive, "On-screen Alt arms with one tap");
            Tap(PadButtons.CaretMode | cross);
            check(!keyboard.AltActive, "Enter consumes armed Alt");
            Key(3, 1);
            Key(3, 1);
            Tap(PadButtons.Triangle);
            check(keyboard.AltActive, "On-screen Alt locks with two taps and survives a space");
            Key(3, 1);
            check(!keyboard.AltActive, "Tapping on-screen Alt again unlocks it");

            Tap(circle);
            Step(ms: 1000);
            Tap(circle);
            check(!keyboard.ShiftActive, "Circle cancels unused QWERTY Shift after one second");
            Tap(layout);
            Tap(circle);
            Step(ms: 1000);
            Tap(circle);
            check(!HasLabel("F1"), "Circle cancels unused alternates after one second");
            foreach (var (column, latch) in new[] { (0, keyboard.controlLatch), (1, keyboard.altLatch), (2, keyboard.shiftLatch) })
            {
                Key(3, column);
                Step(ms: 1000);
                Key(3, column);
                check(!latch.Active, "On-screen " + keyboard.page.Layout.Keys[3, column].Label + " cancels after one second");
            }

            keyboard.Editor.Text = "alpha beta gamma";
            keyboard.Editor.CaretIndex = keyboard.Editor.Text.Length;
            Key(2, 4);
            check(keyboard.Editor.SelectedText == "alpha beta gamma", "All immediately to the right of Paste selects the entire field");
            keyboard.Editor.Select(keyboard.Editor.Text.Length, 0);
            Tap(PadButtons.CaretMode | PadButtons.Square);
            check(keyboard.Editor.Text == "alpha beta ", "R1 Square deletes the previous word");
            int caret = keyboard.Editor.CaretIndex;
            Tap(PadButtons.CaretMode | PadButtons.Left);
            check(keyboard.Editor.CaretIndex == caret - 1 && keyboard.Editor.SelectionLength == 0,
                "R1 D-pad moves the caret without selecting");
            Tap(PadButtons.CaretMode | PadButtons.Layout | PadButtons.Left);
            check(keyboard.Editor.SelectionLength == 1 && ReferenceEquals(keyboard.page, keyboard.symbols),
                "R1 L1 D-pad extends selection without changing the layout");
            int copies = 0, pastes = 0;
            keyboard.Editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { copies++; e.Handled = true; }));
            keyboard.Editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { pastes++; e.Handled = true; }));
            Tap(PadButtons.CaretMode | PadButtons.Triangle);
            Tap(PadButtons.CaretMode | PadButtons.Layout);
            check(copies == 1 && pastes == 0, "R1 Triangle copies and R1 L1 alone does not paste");
            Tap(PadButtons.CaretMode | PadButtons.Layout | PadButtons.Triangle);
            check(pastes == 1 && copies == 1, "R1 L1 Triangle pastes without also copying");

            var overlay = new MainWindow(desktop: true);
            try
            {
                overlay.OpenFromMapper();
                check(overlay.KeyboardVisible && overlay.TouchHide.Visibility == System.Windows.Visibility.Collapsed,
                    "Desktop PadBoard opens with Hide concealed");
                overlay.HandlePanelClick(new System.Windows.Point(100, 50));
                check(overlay.TouchHide.Visibility == System.Windows.Visibility.Visible,
                    "Tapping the keyboard reveals its Hide control");
                System.Windows.Point hide = overlay.TouchHide.TranslatePoint(new System.Windows.Point(16, 16), overlay.Surface);
                bool closed = false;
                overlay.KeyboardClosed += () => closed = true;
                overlay.HandlePanelClick(hide);
                check(!overlay.KeyboardVisible && closed, "Touch Hide notifies the mapper that PadBoard closed");
            }
            finally { overlay.Close(); }
        }
        finally { keyboard.Close(); }
    }
}
