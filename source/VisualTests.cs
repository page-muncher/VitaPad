using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VitaPad;
public sealed partial class MainWindow
{
    internal void LoadPreviewSettings(Settings sample)
    {
        settings=sample; loading=true;
        profiles.ItemsSource=settings.Profiles.Select(p=>p.Name).ToArray();
        profiles.SelectedIndex=settings.ActiveProfile;loading=false;LoadProfile();
    }
    // Presentation replay never injects OS input. Engine routing is checked separately.
    double replayNow;
    internal void ReplayPreview(Pad? pad, double advance=0)
    {
        replayNow+=advance;
        engine.Tick(pad,Current,true,.008);SyncPopups(pad,replayNow);
    }
    internal bool ReferenceVisible=>overlay.IsVisible;
    internal bool ConfirmationVisible=>exitPrompt.IsVisible;
    internal LayerOverlayWindow Reference=>overlay;
    internal ExitPromptWindow Confirmation=>exitPrompt;
    internal void OpenPreviewEditor()=>ShowEditor();

    internal void CheckStartShortcut(Action<bool,string> check, PadBoardTests.Recorder output)
    {
        const ushort start = 0x0010;
        var fixture = new Settings();
        fixture.Profiles[0].Layers[0].Bindings["Start"] = new Binding { Keys = [70] };
        LoadPreviewSettings(fixture);
        Select("A");
        output.Events.Clear();

        void Step(double now, ushort buttons = 0) => ProcessControllerFrame(new Pad { Buttons = buttons }, now, .008);

        Step(0);
        Step(.10, start);
        Step(.30);
        check(IsVisible && selected == "Start", "Tapping Start selects its binding without hiding the studio");
        Step(1, start);
        Step(1.999, start);
        check(IsVisible, "Holding Start for less than a full second leaves the studio open");
        Step(2, start);
        check(!IsVisible, "Holding Start for a full second sends the studio to the tray");
        Step(2.5, start);
        check(output.Events.Count == 0, "The held Start cannot fire its tray binding during the transition");
        Step(3);
        Step(3.1, start);
        check(output.Events.SequenceEqual(new[] { "K70:True" }), "A fresh Start press can still use its tray binding");

        ShowEditor();
        Step(3.2, start);
        Step(4.3, start);
        check(IsVisible, "Reopening the studio while Start is held requires a new press");
        Step(5);
        Step(6, start);
        ProcessControllerFrame(null, 6.7, .008);
        Step(7, start);
        Step(8.1, start);
        check(IsVisible, "Disconnect cancels the hold and the replacement controller must release Start");
        Step(9);
        Step(10, start);
        Step(11, start | 0x0380);
        check(IsVisible && ConfirmationVisible, "Exit confirmation takes priority over a completed Start hold");
        Step(11.1, start);
        Step(11.2, start | 0x2000);
        Step(12.5, start);
        check(IsVisible && !ConfirmationVisible, "Cancelling the exit prompt keeps the held Start from hiding the studio");
        Step(13);
        Step(14, start);
        Step(15, start);
        check(!IsVisible, "Start can send the studio to the tray again after release");
        Step(15.1);
        output.Events.Clear();
    }

    internal void CheckPadBoard(Action<bool,string> check, PadBoardTests.Recorder output)
    {
        const ushort r3 = 0x80, l1 = 0x100, r1 = 0x200, cross = 0x1000;
        var fixture = new Settings();
        var profile = fixture.Profiles[0];
        profile.Layers[0].Bindings["A"] = new Binding { Keys = [18, 76], Mouse = ["Left click"] };
        profile.Layers[0].Bindings["Y"] = new Binding { Keys = [65] };
        LoadPreviewSettings(fixture);
        Select("R3");
        bindingType.SelectedIndex = 1;
        check(profile.Layers[0].Bindings["R3"].PadBoard && actions.Visibility == Visibility.Collapsed,
            "Binding editor assigns PadBoard to Base R3");
        Select("A");
        Select("R3");
        check(bindingType.SelectedIndex == 1, "Binding editor restores the PadBoard selection");

        void Step(ushort buttons = 0)
        {
            RouteController(new Pad { Buttons = buttons }, .008);
            SyncPopups(referenceState, replayNow += .008);
        }

        Step();
        Step(r3);
        check(!padBoardOpen, "Studio keeps PadBoard closed while editing");
        Hide();
        Step();
        Step(cross);
        output.Events.Clear();
        var before = VisualTests.GetForegroundWindow();
        Step(cross | r3);
        check(padBoardOpen && !ReferenceVisible && VisualTests.GetForegroundWindow() == before,
            "Base R3 opens the integrated keyboard without stealing focus");
        check(output.Events.SequenceEqual(new[] { "K76:False", "K18:False", "MLeft click:False" }),
            "Integrated handoff releases the VitaPad chord and mouse button");
        output.Events.Clear();
        for (int frame = 0; frame < 12; frame++) Step(cross | r3);
        check(padBoardOpen && output.Events.Count == 0, "Held opening inputs stay consumed in PadBoard");
        Step(cross);
        Step(cross | l1);
        check(padBoard!.FindName("KeyboardPages") is Grid pages &&
              pages.Children[0].Visibility == Visibility.Collapsed && pages.Children[1].Visibility == Visibility.Visible,
            "First L1 press switches the integrated keyboard layout");
        Step(cross);
        Step(cross | r3);
        Step(cross);
        check(!padBoardOpen && output.Events.Count == 0 && VisualTests.GetForegroundWindow() == before,
            "Second R3 closes the keyboard while holding Cross without leaking output or taking focus");
        Step(cross | 0x8000);
        check(output.Events.SequenceEqual(new[] { "K65:True" }), "A fresh VitaPad binding works immediately while held Cross stays blocked");
        Step();
        output.Events.Clear();
        Step(cross);
        check(output.Events.SequenceEqual(new[] { "K18:True", "K76:True", "MLeft click:True" }), "Cross works normally after release and re-press");
        Step();
        Step(r3);
        Step();
        Step(l1 | r1 | r3);
        check(!padBoardOpen && ConfirmationVisible, "VitaPad exit confirmation takes priority in PadBoard mode");
        Step();
        Step(0x2000);
        Step();
        Step(r3);
        RouteController(null, 0);
        SyncPopups(referenceState, replayNow);
        check(!padBoardOpen, "Disconnect closes PadBoard");
        Step(r3);
        check(!padBoardOpen, "Reconnect with R3 held does not open PadBoard");
        Step();
        Step(r3);
        check(padBoardOpen, "Fresh R3 reopens PadBoard after reconnect");
    }
}
public static class VisualTests
{
    [DllImport("user32.dll")]internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern nint GetWindowLongPtr(nint hwnd,int index);
    [DllImport("user32.dll")]static extern bool GetWindowRect(nint hwnd,out NativeRect rectangle);
    [StructLayout(LayoutKind.Sequential)]record struct NativeRect(int Left,int Top,int Right,int Bottom);
    static NativeRect Bounds(Window window)
    {
        if(!GetWindowRect(new WindowInteropHelper(window).Handle,out var bounds))throw new InvalidOperationException("Cannot read overlay bounds.");
        return bounds;
    }
    public static void SaveImage(Window window,string path)
    {
        window.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=File.Create(path);encoder.Save(stream);
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(parent,i)))yield return child;
    }
    static bool TextFits(Window window)
    {
        foreach(var text in Descendants(window).OfType<TextBlock>().Where(t=>t.ActualWidth>0))
        {
            var measure=new TextBlock{Text=text.Text,FontFamily=text.FontFamily,FontSize=text.FontSize,FontWeight=text.FontWeight,TextWrapping=text.TextWrapping};
            measure.Measure(new Size(text.ActualWidth,double.PositiveInfinity));
            if(measure.DesiredSize.Height>text.ActualHeight+1)return false;
        }
        return true;
    }
    public static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var report=new List<string>();int count=0;
        var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        MainWindow? main=null;LayerOverlayWindow? stress=null;
        try
        {
            void Check(bool pass,string name){if(!pass)throw new Exception(name);count++;report.Add("PASS "+name);}
            var settingsFile=Path.Combine(Settings.ExecutableDirectory,"settings.json");
            var sample=File.Exists(settingsFile)?Settings.Parse(File.ReadAllText(settingsFile)):new Settings();
            var output=new PadBoardTests.Recorder();
            main=new MainWindow(true,false,output);app.MainWindow=main;main.LoadPreviewSettings(sample);
            main.WindowState=WindowState.Maximized;main.Show();main.UpdateLayout();
            Check(main.IsVisible&&main.WindowState==WindowState.Maximized,"Studio opens maximized");
            main.ReplayPreview(new Pad{Buttons=0x100});
            Check(!main.ReferenceVisible,"No layer overlay while studio is visible");
            SaveImage(main,Path.Combine(directory,"studio.png"));
            main.WindowState=WindowState.Minimized;
            Check(!main.IsVisible,"Minimize sends studio to tray");
            var before=GetForegroundWindow();main.ReplayPreview(new Pad{Buttons=0x100});
            main.ReplayPreview(new Pad{Buttons=0x100},.49);
            Check(!main.ReferenceVisible,"Tray L1 stays hidden before half-second delay");
            main.ReplayPreview(new Pad{Buttons=0x100},.02);main.Reference.UpdateLayout();
            Check(main.ReferenceVisible,"Quiet tray L1 displays its reference after the delay");
            Check(GetForegroundWindow()==before,"Reference does not activate or steal foreground focus");
            var style=GetWindowLongPtr(new WindowInteropHelper(main.Reference).Handle,-20).ToInt64();
            Check((style&0x08000020L)==0x08000020L&&!main.Reference.ShowInTaskbar,"Reference has native no-activate and click-through flags");
            SaveImage(main.Reference,Path.Combine(directory,"overlay-l1.png"));
            Check(TextFits(main.Reference),"L1 labels fit their measured cards");
            Check(Math.Abs(main.Reference.Opacity-.75)<.001,"Entire overlay uses 75% opacity");
            var l1Bounds=Bounds(main.Reference);
            var workArea=System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(main.Reference).Handle).WorkingArea;
            Check(l1Bounds.Right==workArea.Right-14&&l1Bounds.Bottom==workArea.Bottom-14,"Overlay sits 14 physical pixels from the bottom-right work-area corner");
            for(int i=0;i<24;i++)main.ReplayPreview(new Pad{Buttons=0x100},.008);
            Check(Bounds(main.Reference)==l1Bounds,"Repeated frames leave the overlay in exactly the same position");
            main.ReplayPreview(new Pad{Buttons=0x200});
            Check(main.ReferenceVisible,"Changing from L1 to R1 keeps the preview displayed immediately");
            main.ReplayPreview(new Pad{Buttons=0x200},.51);
            SaveImage(main.Reference,Path.Combine(directory,"overlay-r1.png"));
            Check(main.ReferenceVisible&&TextFits(main.Reference),"R1 reference updates with fitting labels");
            var r1Bounds=Bounds(main.Reference);
            Check(r1Bounds.Right==l1Bounds.Right&&r1Bounds.Bottom==l1Bounds.Bottom,"R1 keeps the same bottom-right anchor as L1");
            main.ReplayPreview(new Pad{Buttons=0x300});
            Check(main.ReferenceVisible,"Adding L1 while R1 is held keeps the preview displayed immediately");
            main.ReplayPreview(new Pad{Buttons=0x300},.51);
            SaveImage(main.Reference,Path.Combine(directory,"overlay-base.png"));
            Check(main.ReferenceVisible&&TextFits(main.Reference),"Both bumpers display base reference with fitting labels");
            var baseBounds=Bounds(main.Reference);
            Check(baseBounds.Right==l1Bounds.Right&&baseBounds.Bottom==l1Bounds.Bottom,"Both-bumper hint keeps the same bottom-right anchor");
            main.ReplayPreview(new Pad());Check(!main.ReferenceVisible,"Reference disappears on bumper release");
            main.ReplayPreview(new Pad{Buttons=0x100});
            main.ReplayPreview(new Pad{Buttons=0x300},.3);
            main.ReplayPreview(new Pad{Buttons=0x200},.19);
            Check(!main.ReferenceVisible,"Changing bumpers during the delay preserves the pending preview");
            main.ReplayPreview(new Pad{Buttons=0x200},.02);
            Check(main.ReferenceVisible,"The preview appears at the original deadline after changing bumpers");
            main.ReplayPreview(new Pad());
            main.ReplayPreview(new Pad{Buttons=0x100});main.ReplayPreview(new Pad{Buttons=0x100},.51);main.ReplayPreview(null);Check(!main.ReferenceVisible,"Reference disappears on disconnect");
            var bindingsFixture=new Settings();
            bindingsFixture.Profiles[0].Layers[1].Bindings["A"]=new Binding{Keys=[8]};
            main.LoadPreviewSettings(bindingsFixture);
            main.ReplayPreview(new Pad{Buttons=0x110}); // L1 + unassigned Start
            main.ReplayPreview(new Pad{Buttons=0x110},.51);
            Check(main.ReferenceVisible,"Holding unassigned Start does not prevent the hint");
            main.ReplayPreview(new Pad{Buttons=0x1100}); // L1 + Cross, assigned Backspace
            Check(!main.ReferenceVisible,"Pressing assigned Backspace hides the hint immediately");
            main.ReplayPreview(new Pad{Buttons=0x100},.6);
            Check(!main.ReferenceVisible,"Hint stays hidden after Backspace release with L1 still held");
            main.ReplayPreview(new Pad());main.ReplayPreview(new Pad{Buttons=0x100});main.ReplayPreview(new Pad{Buttons=0x100},.51);
            Check(main.ReferenceVisible,"Hint returns after bumpers release and a new hold");
            main.LoadPreviewSettings(sample);
            main.ReplayPreview(new Pad());main.ReplayPreview(new Pad{Buttons=0x380});
            Check(main.ConfirmationVisible&&!main.ReferenceVisible,"Exit prompt replaces layer overlay");
            SaveImage(main.Confirmation,Path.Combine(directory,"exit-prompt.png"));
            main.ReplayPreview(new Pad{Buttons=0x380});main.ReplayPreview(new Pad{Buttons=0x2380});
            Check(!main.ConfirmationVisible,"Circle dismisses the exit prompt");
            main.ReplayPreview(new Pad{Buttons=0x380});Check(!main.ConfirmationVisible,"Held chord cannot reopen cancelled popup");
            main.OpenPreviewEditor();Check(main.WindowState==WindowState.Maximized&&!main.ReferenceVisible,"Reopening studio maximizes and hides reference");
            main.ReplayPreview(new Pad{Buttons=0x100});Check(!main.ReferenceVisible,"Editor keeps overlays hidden after reopening");
            main.Hide();
            var crowded=new Profile{Name="All controls · long chord stress"};
            foreach(var id in Engine.Ids)crowded.Layers[1].Bindings[id]=new Binding{Keys=[162,160,164,91,65,66,67,68,69,70,71,72,73,74,75,76],Mouse=["Left click","Wheel Down"]};
            stress=new LayerOverlayWindow();before=GetForegroundWindow();stress.ShowLayer(crowded,1,false,new HashSet<string>());stress.UpdateLayout();
            Check(GetForegroundWindow()==before,"Dense overlay also preserves foreground focus");
            Check(TextFits(stress),"All 22 long-chord callouts fit without text clipping");
            SaveImage(stress,Path.Combine(directory,"overlay-stress.png"));
            stress.Close();stress=null;
            main.OpenPreviewEditor();
            main.CheckStartShortcut(Check,output);
            main.OpenPreviewEditor();
            ControllerKeyboard.MainWindow.CheckModifiers(Check);
            main.CheckPadBoard(Check,output);
            report.Add($"PASS: {count} visual/integration assertions");File.WriteAllLines(Path.Combine(directory,"visual-tests.txt"),report);return 0;
        }
        catch(Exception ex){report.Add("FAIL "+ex);File.WriteAllLines(Path.Combine(directory,"visual-tests.txt"),report);return 1;}
        finally{stress?.Close();main?.Cleanup();app.Shutdown();}
    }
}
