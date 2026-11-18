using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WApplication=System.Windows.Application;
using Button=System.Windows.Controls.Button;
using ComboBox=System.Windows.Controls.ComboBox;
using TextBox=System.Windows.Controls.TextBox;
using Orientation=System.Windows.Controls.Orientation;
using Forms=System.Windows.Forms;

namespace VitaPad;
public static class Program {
 [STAThread] public static int Main(string[] args) {
  if(args.Contains("--self-test"))return Tests.Run(args.Last());
  if(args.Contains("--visual-test"))return VisualTests.Run(args.Last());
  using var instance=new SingleInstance("Local\\VitaPad.DesktopMapper");
  if(!instance.TryAcquire())return 0;
  var app=new WApplication{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.DispatcherUnhandledException+=(_,e)=>{System.Windows.MessageBox.Show(e.Exception.Message,"VitaPad error");e.Handled=true;app.Shutdown(1);};
  var main=new MainWindow(args.Contains("--preview")); app.MainWindow=main;
  if(!args.Contains("--preview")) main.WindowState=WindowState.Maximized;
  instance.Listen(()=>{
   if(!app.Dispatcher.HasShutdownStarted)
    app.Dispatcher.BeginInvoke(new Action(main.ShowEditor));
  });
  main.Show();
  app.Exit+=(_,_)=>main.Cleanup(); return app.Run();
 }
}
public sealed partial class MainWindow:Window {
 static readonly SolidColorBrush Bg=Brush("#141617"),Panel=Brush("#1E2124"),Line=Brush("#383D42"),White=Brush("#F3F2EF"),Muted=Brush("#9CA3AA"),Accent=Brush("#F26972");
 public static SolidColorBrush Brush(string hex)=>new((Color)ColorConverter.ConvertFromString(hex));
 readonly string dataPath=System.IO.Path.Combine(Settings.ExecutableDirectory,"settings.json");
 Settings settings=new(); readonly NativeOutput native=new(); readonly Engine engine; readonly Forms.NotifyIcon tray; readonly DispatcherTimer timer; readonly Stopwatch watch=Stopwatch.StartNew(); double last;
 readonly ComboBox profiles=new(),layers=new(),leftMode=new(),rightMode=new(); readonly TextBlock status=new(),live=new(),bindingTitle=new(); readonly Canvas canvas=new(){Width=1120,Height=440};
 readonly Dictionary<string,Button> controls=[]; readonly Dictionary<string,Shape> indicators=[];
 readonly StackPanel editor=new(); readonly ListBox actions=new(){Height=100}; readonly TextBox rename=new(); readonly TextBlock details=new(); readonly Slider dead=new(){Minimum=5,Maximum=90},trigger=new(){Minimum=5,Maximum=95},speed=new(){Minimum=50,Maximum=4000};
 readonly Button captureButton=new(); readonly HashSet<int> captureKeys=[]; readonly List<int> draftKeys=[]; readonly List<string> draftMouse=[];
 readonly ComboBox bindingType=new(){ItemsSource=new[]{"Keyboard / mouse","PadBoard"},Width=180,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,8,0,0)};
 readonly WrapPanel keyActions=new(),mouseActions=new();
 bool draftPadBoard,loadingBinding;
 double? startHoldBegan;
 bool startNeedsRelease=true;
 string selected="A"; bool loading=true,capturing,exiting,paused,preview; uint? controller; string? lastDetected; readonly ToolTip tip=new(); readonly LayerOverlayWindow overlay=new(); readonly HintGate hint=new(); readonly ExitPromptWindow exitPrompt=new();
 Profile Current=>settings.Profiles[settings.ActiveProfile]; Layer Editing=>Current.Layers[Math.Max(0,layers.SelectedIndex)];
 public MainWindow(bool previewMode, bool automaticPreview=true) : this(previewMode,automaticPreview,null) {}
 internal MainWindow(bool previewMode, bool automaticPreview, IOutput? output) {
  preview=previewMode; engine=new(output??native); Title="VitaPad · Controller studio"; Width=1240;Height=1040;MinWidth=1020;MinHeight=780;Background=Bg;Foreground=White;FontFamily=new("Segoe UI");FontSize=14;WindowStartupLocation=WindowStartupLocation.CenterScreen;
  Resources.Add(typeof(Button),ButtonStyle()); Resources.Add(typeof(ComboBox),ComboStyle()); Resources.Add(typeof(TextBox),TextStyle());
  if(!preview && File.Exists(dataPath))try{settings=Settings.Parse(File.ReadAllText(dataPath));}catch(Exception e){Forms.MessageBox.Show("Your settings could not be loaded. The original file has been preserved. VitaPad will exit.\n\n"+e.Message,"VitaPad");throw;}
  else settings.Profiles[0].Layers[0].Bindings["R3"]=new Binding{PadBoard=true};
  var root=new Grid{Margin=new Thickness(30,22,30,22)}; Content=root; root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var header=new DockPanel{Margin=new Thickness(0,0,0,22)}; var brand=new StackPanel(); brand.Children.Add(T("VITA PAD",30,White,true));brand.Children.Add(T("Your handheld. Your desktop.",13,Muted));header.Children.Add(brand);
  var tools=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};DockPanel.SetDock(tools,Dock.Right);tools.Children.Add(B("Import",Import));tools.Children.Add(B("Export",Export));tools.Children.Add(B("To tray",()=>Hide()));header.Children.Add(tools);root.Children.Add(header);
  var bar=new Grid{Margin=new Thickness(0,0,0,16)};bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
  var ps=new StackPanel();ps.Children.Add(T("UNIVERSAL PROFILE",11,Muted,true));profiles.ItemsSource=settings.Profiles.Select(p=>p.Name).ToArray();profiles.SelectedIndex=settings.ActiveProfile;profiles.SelectionChanged+=(_,_)=>{if(loading||profiles.SelectedIndex<0)return;engine.Reset();settings.ActiveProfile=profiles.SelectedIndex;LoadProfile();Save();};ps.Children.Add(profiles);bar.Children.Add(ps);
  var ls=new StackPanel{Margin=new Thickness(16,0,16,0)};ls.Children.Add(T("EDIT LAYER",11,Muted,true));layers.ItemsSource=new[]{"Base · no bumper","Layer 1 · hold L1","Layer 2 · hold R1"};layers.SelectedIndex=0;layers.SelectionChanged+=(_,_)=>{if(!loading){LoadModes();Select(selected);RefreshLabels();}};ls.Children.Add(layers);Grid.SetColumn(ls,1);bar.Children.Add(ls);
  var ss=new StackPanel();ss.Children.Add(T("CONTROLLER",11,Muted,true));status.Text="Waiting for Xbox controller…";status.Margin=new Thickness(4,12,0,0);status.Foreground=Accent;ss.Children.Add(status);Grid.SetColumn(ss,2);bar.Children.Add(ss);Grid.SetRow(bar,1);root.Children.Add(bar);
  var body=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};var bodyStack=new StackPanel();body.Content=bodyStack;Grid.SetRow(body,2);root.Children.Add(body);
  var diagramBorder=new Border{Background=Panel,CornerRadius=new CornerRadius(18),Padding=new Thickness(10),BorderBrush=Line,BorderThickness=new Thickness(1)};var diagramStack=new StackPanel();diagramBorder.Child=diagramStack;
  var diagramHeader=new DockPanel{Margin=new Thickness(14,8,14,0)};diagramHeader.Children.Add(T("CONTROL MAP",12,White,true));live.Text="Press a controller input to select its binding";live.Foreground=Muted;live.HorizontalAlignment=HorizontalAlignment.Right;diagramHeader.Children.Add(live);diagramStack.Children.Add(diagramHeader);
  var view=new Viewbox{Stretch=Stretch.Uniform,Height=370};view.Child=canvas;diagramStack.Children.Add(view);bodyStack.Children.Add(diagramBorder);DrawController();
  var lower=new Grid{Margin=new Thickness(0,18,0,0)};lower.ColumnDefinitions.Add(new(){Width=new GridLength(1.25,GridUnitType.Star)});lower.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bodyStack.Children.Add(lower);
  var editBorder=new Border{Background=Panel,CornerRadius=new CornerRadius(14),Padding=new Thickness(20),Margin=new Thickness(0,0,14,0)};editBorder.Child=editor;lower.Children.Add(editBorder);
  editor.Children.Add(bindingTitle);bindingTitle.FontSize=21;bindingTitle.FontWeight=FontWeights.SemiBold;
  editor.Children.Add(bindingType);bindingType.SelectionChanged+=(_,_)=>{if(loadingBinding)return;if(capturing)CancelCapture();draftPadBoard=bindingType.SelectedIndex==1;ApplyDraft();};
  actions.Margin=new Thickness(0,10,0,8);actions.Background=Bg;actions.Foreground=White;actions.BorderBrush=Line;editor.Children.Add(actions);
  captureButton.Content="Record key / chord";captureButton.Click+=(_,_)=>BeginCapture();keyActions.Children.Add(captureButton);
  var keyPicker=new ComboBox{Width=130,Margin=new Thickness(4)};var keyValues=Enum.GetValues<Forms.Keys>().Select(k=>(int)k).Where(k=>k>=8&&k<=254).Distinct().OrderBy(Engine.KeyName).ToArray();keyPicker.ItemsSource=keyValues.Select(Engine.KeyName).ToArray();keyPicker.SelectedIndex=Array.IndexOf(keyValues,65);keyActions.Children.Add(keyPicker);keyActions.Children.Add(B("Add key",()=>{draftKeys.Add(keyValues[keyPicker.SelectedIndex]);ApplyDraft();}));editor.Children.Add(keyActions);
  var mousePicker=new ComboBox{Width=155,Margin=new Thickness(4),ItemsSource=Engine.MouseActions,SelectedIndex=0};mouseActions.Children.Add(mousePicker);mouseActions.Children.Add(B("Add mouse",()=>{draftMouse.Add((string)mousePicker.SelectedItem);ApplyDraft();}));mouseActions.Children.Add(B("Remove selected",()=>{int i=actions.SelectedIndex;if(i<0)return;if(i<draftKeys.Count)draftKeys.RemoveAt(i);else draftMouse.RemoveAt(i-draftKeys.Count);ApplyDraft();}));mouseActions.Children.Add(B("Clear",()=>{draftKeys.Clear();draftMouse.Clear();ApplyDraft();}));editor.Children.Add(mouseActions);
  details.Text="Outputs are paused while this editor is open.";details.Foreground=Muted;details.TextWrapping=TextWrapping.Wrap;details.Margin=new Thickness(0,10,0,0);editor.Children.Add(details);
  var configBorder=new Border{Background=Panel,CornerRadius=new CornerRadius(14),Padding=new Thickness(20)};var config=new StackPanel();configBorder.Child=config;Grid.SetColumn(configBorder,1);lower.Children.Add(configBorder);config.Children.Add(T("Profile tuning",21,White,true));
  rename.Text=Current.Name;rename.Margin=new Thickness(0,10,0,6);rename.LostFocus+=(_,_)=>{if(loading)return;var name=rename.Text.Trim();if(name.Length==0||name.Length>40){rename.Text=Current.Name;return;}Current.Name=name;int index=settings.ActiveProfile;loading=true;profiles.ItemsSource=settings.Profiles.Select(p=>p.Name).ToArray();profiles.SelectedIndex=index;loading=false;Save();};config.Children.Add(rename);
  var modes=new Grid();modes.ColumnDefinitions.Add(new());modes.ColumnDefinitions.Add(new());var lm=new StackPanel();lm.Children.Add(T("Left stick",12,Muted));leftMode.ItemsSource=new[]{"Keys","Pointer","Scroll"};lm.Children.Add(leftMode);modes.Children.Add(lm);var rm=new StackPanel{Margin=new Thickness(8,0,0,0)};rm.Children.Add(T("Right stick",12,Muted));rightMode.ItemsSource=new[]{"Keys","Pointer","Scroll"};rm.Children.Add(rightMode);Grid.SetColumn(rm,1);modes.Children.Add(rm);config.Children.Add(modes);
  leftMode.SelectionChanged+=(_,_)=>ModeChanged();rightMode.SelectionChanged+=(_,_)=>ModeChanged();
  AddSlider(config,"Stick dead zone",dead,()=>{Current.DeadZone=dead.Value/100;});AddSlider(config,"Trigger threshold",trigger,()=>{Current.TriggerThreshold=trigger.Value/100;});AddSlider(config,"Pointer speed · px / second",speed,()=>{Current.MouseSpeed=speed.Value;});
  var footer=new DockPanel{Margin=new Thickness(0,16,0,0)};footer.Children.Add(T("L1 + R1 = base layer  ·  + R3 = Exit?  ·  Hold Start 1s = tray",12,Muted));var version=T("VitaPad 1.5.1  /  AUTO-SAVED",11,Muted);version.HorizontalAlignment=HorizontalAlignment.Right;footer.Children.Add(version);Grid.SetRow(footer,3);root.Children.Add(footer);
  tray=new Forms.NotifyIcon{Icon=new System.Drawing.Icon(typeof(MainWindow).Assembly.GetManifestResourceStream("VitaPad.VitaPad.ico")!),Visible=!preview,Text="VitaPad · waiting for controller"};var menu=new Forms.ContextMenuStrip();menu.Items.Add("Open controller studio",null,(_,_)=>ShowEditor());var profileMenu=new Forms.ToolStripMenuItem("Profile");for(int i=0;i<3;i++){int index=i;profileMenu.DropDownItems.Add(settings.Profiles[i].Name,null,(_,_)=>{profiles.SelectedIndex=index;});}menu.Items.Add(profileMenu);menu.Opening+=(_,_)=>{for(int j=0;j<3;j++){profileMenu.DropDownItems[j].Text=settings.Profiles[j].Name;((Forms.ToolStripMenuItem)profileMenu.DropDownItems[j]).Checked=j==settings.ActiveProfile;}};menu.Items.Add("Pause / resume mappings",null,(_,_)=>{paused=!paused;engine.Reset();hint.Reset();overlay.Hide();});menu.Items.Add("Exit VitaPad",null,(_,_)=>Exit());tray.ContextMenuStrip=menu;tray.MouseClick+=(_,e)=>{if(e.Button==Forms.MouseButtons.Left)ShowEditor();};
  exitPrompt.CancelRequested+=()=>{engine.Exit.Cancel();engine.Reset();exitPrompt.Hide();IsEnabled=true;};
  StateChanged+=(_,_)=>{if(WindowState==WindowState.Minimized){engine.Reset();Hide();}};
  PreviewKeyDown+=RecordDown;PreviewKeyUp+=RecordUp;Deactivated+=(_,_)=>{if(capturing)CancelCapture();};
  IsVisibleChanged+=(_,_)=>{if(startHoldBegan.HasValue)startNeedsRelease=true;startHoldBegan=null;lastDetected=null;capturing=false;captureKeys.Clear();captureButton.Content="Record key / chord";StopPadBoard();engine.Reset();hint.Reset();overlay.Hide();};Closing+=(_,e)=>{if(!exiting){e.Cancel=true;Hide();}};
  timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(8)};timer.Tick+=(_,_)=>Poll();LoadProfile();loading=false;Select("A");RefreshLabels();
  if(!preview)controllerClock=ControllerKeyboard.DesktopPollClock.TryStart(Dispatcher,Poll,false);
  if(controllerClock is null)timer.Start();
  if(preview && automaticPreview)Loaded+=(_,_)=>{var shotTimer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(400)};shotTimer.Tick+=(_,_)=>{shotTimer.Stop();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(System.IO.Path.Combine(AppContext.BaseDirectory,"VitaPad-preview.png")))encoder.Save(f);Exit();};shotTimer.Start();};
 }
 static TextBlock T(string text,double size,Brush color,bool bold=false)=>new(){Text=text,FontSize=size,Foreground=color,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center};
 Button B(string label,Action action){var b=new Button{Content=label};b.Click+=(_,_)=>action();return b;}
 Style ButtonStyle(){var s=new Style(typeof(Button));s.Setters.Add(new Setter(Control.BackgroundProperty,Brush("#292E33")));s.Setters.Add(new Setter(Control.ForegroundProperty,White));s.Setters.Add(new Setter(Control.BorderBrushProperty,Line));s.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,8,12,8)));s.Setters.Add(new Setter(FrameworkElement.MarginProperty,new Thickness(3)));s.Setters.Add(new Setter(Control.CursorProperty,Cursors.Hand));var template=new ControlTemplate(typeof(Button));var border=new FrameworkElementFactory(typeof(Border));border.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));border.SetValue(Border.BorderThicknessProperty,new Thickness(1));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);template.VisualTree=border;var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.BorderBrushProperty,Accent));template.Triggers.Add(hover);s.Setters.Add(new Setter(Control.TemplateProperty,template));return s;}
 Style ComboStyle(){var s=new Style(typeof(ComboBox));s.Setters.Add(new Setter(Control.ForegroundProperty,White));s.Setters.Add(new Setter(Control.BackgroundProperty,Panel));s.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(10,8,10,8)));s.Setters.Add(new Setter(FrameworkElement.MinHeightProperty,36.0));var itemStyle=new Style(typeof(ComboBoxItem));itemStyle.Setters.Add(new Setter(Control.BackgroundProperty,Panel));itemStyle.Setters.Add(new Setter(Control.ForegroundProperty,White));s.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty,itemStyle));var template=new ControlTemplate(typeof(ComboBox));var grid=new FrameworkElementFactory(typeof(Grid));var toggle=new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.ToggleButton));toggle.SetValue(Control.BackgroundProperty,Brush("#292E33"));toggle.SetValue(Control.ForegroundProperty,White);toggle.SetValue(Control.BorderBrushProperty,Line);toggle.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new System.Windows.Data.Binding("IsDropDownOpen"){RelativeSource=new(System.Windows.Data.RelativeSourceMode.TemplatedParent),Mode=System.Windows.Data.BindingMode.TwoWay});var label=new FrameworkElementFactory(typeof(ContentPresenter));label.SetValue(FrameworkElement.MarginProperty,new Thickness(10,8,24,8));label.SetValue(UIElement.IsHitTestVisibleProperty,false);label.SetValue(ContentPresenter.ContentProperty,new TemplateBindingExtension(System.Windows.Controls.Primitives.Selector.SelectedItemProperty));var arrow=new FrameworkElementFactory(typeof(TextBlock));arrow.SetValue(TextBlock.TextProperty,"⌄");arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Right);arrow.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);arrow.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,10,0));arrow.SetValue(UIElement.IsHitTestVisibleProperty,false);var popup=new FrameworkElementFactory(typeof(System.Windows.Controls.Primitives.Popup)){Name="PART_Popup"};popup.SetValue(System.Windows.Controls.Primitives.Popup.StaysOpenProperty,false);popup.SetValue(System.Windows.Controls.Primitives.Popup.PlacementProperty,System.Windows.Controls.Primitives.PlacementMode.Bottom);popup.SetValue(System.Windows.Controls.Primitives.Popup.AllowsTransparencyProperty,true);popup.SetValue(System.Windows.Controls.Primitives.Popup.IsOpenProperty,new TemplateBindingExtension(ComboBox.IsDropDownOpenProperty));var itemsBorder=new FrameworkElementFactory(typeof(Border));itemsBorder.SetValue(Border.BackgroundProperty,Panel);itemsBorder.SetValue(Border.BorderBrushProperty,Line);itemsBorder.SetValue(Border.BorderThicknessProperty,new Thickness(1));var scroll=new FrameworkElementFactory(typeof(ScrollViewer));scroll.SetValue(FrameworkElement.MaxHeightProperty,280.0);var presenter=new FrameworkElementFactory(typeof(ItemsPresenter));scroll.AppendChild(presenter);itemsBorder.AppendChild(scroll);popup.AppendChild(itemsBorder);grid.AppendChild(toggle);grid.AppendChild(label);grid.AppendChild(arrow);grid.AppendChild(popup);template.VisualTree=grid;s.Setters.Add(new Setter(Control.TemplateProperty,template));return s;}
 Style TextStyle(){var s=new Style(typeof(TextBox));s.Setters.Add(new Setter(Control.BackgroundProperty,Bg));s.Setters.Add(new Setter(Control.ForegroundProperty,White));s.Setters.Add(new Setter(Control.BorderBrushProperty,Line));s.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(8)));return s;}
 void AddSlider(StackPanel parent,string title,Slider slider,Action update){var label=T(title,12,Muted);label.Margin=new Thickness(0,8,0,0);parent.Children.Add(label);slider.TickFrequency=1;slider.IsSnapToTickEnabled=true;slider.ValueChanged+=(_,_)=>{label.Text=title+"  ·  "+Math.Round(slider.Value);if(!loading){update();engine.Reset();Save();}};parent.Children.Add(slider);}
 void DrawSvg(){
  using var stream=typeof(MainWindow).Assembly.GetManifestResourceStream("VitaPad.Controller.svg")!;
  var svg=System.Xml.Linq.XDocument.Load(stream);
  foreach(var el in svg.Root!.Elements()){
   double N(string a,double fallback=0)=>double.TryParse((string?)el.Attribute(a),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:fallback;
   Brush? Paint(string a)=>el.Attribute(a)==null || (string?)el.Attribute(a)=="none"?null:Brush((string)el.Attribute(a)!);
   FrameworkElement? element=null;double x=N("x"),y=N("y");
   switch(el.Name.LocalName){
    case "rect":element=new Rectangle{Width=N("width"),Height=N("height"),RadiusX=N("rx"),RadiusY=N("rx")};break;
    case "ellipse":element=new Ellipse{Width=2*N("rx"),Height=2*N("ry")};x=N("cx")-N("rx");y=N("cy")-N("ry");break;
    case "path":element=new System.Windows.Shapes.Path{Data=Geometry.Parse((string)el.Attribute("d")!)};break;
    case "text":var size=N("font-size",12);element=T(el.Value,size,Paint("fill")??White);y-=size;break;
   }
   if(element==null)continue;if(element is Shape shape){shape.Fill=Paint("fill");shape.Stroke=Paint("stroke");shape.StrokeThickness=N("stroke-width",1);}Canvas.SetLeft(element,x);Canvas.SetTop(element,y);canvas.Children.Add(element);
  }
 }
 void DrawController(){
  void Shape(Shape s,double x,double y){Canvas.SetLeft(s,x);Canvas.SetTop(s,y);canvas.Children.Add(s);} void Text(string value,double x,double y,double size=12,Brush? color=null){var t=T(value,size,color??Muted);Canvas.SetLeft(t,x);Canvas.SetTop(t,y);canvas.Children.Add(t);}
  DrawSvg();
  foreach(var (id,x,y) in new[]{("L3",309d,258d),("R3",755d,258d)}){var ellipse=new Ellipse{Width=54,Height=54,Fill=Brush("#30353A"),Stroke=Accent,StrokeThickness=2};Shape(ellipse,x,y);var hit=new Button{Content=id,Width=46,Height=46,Background=Brush("#24282D"),Padding=new Thickness(0),Margin=new Thickness(0)};hit.Click+=(_,_)=>Select(id);ShapeButton(hit,x+4,y+4);indicators[id]=ellipse;}
  foreach(var (id,x,y) in new[]{("Y",773d,145d),("X",748d,171d),("B",798d,171d),("A",773d,197d)}){var b=new Button{Content=Engine.DisplayName(id),Width=26,Height=26,Padding=new Thickness(0),Margin=new Thickness(0),FontSize=17,Background=Brush("#343A40")};b.Click+=(_,_)=>Select(id);ShapeButton(b,x,y);}
  Shape(new Ellipse{Width=12,Height=12,Fill=Brush("#555B61")},320,127);Shape(new Ellipse{Width=12,Height=12,Fill=Brush("#555B61")},791,127);
  Text("L1 / LAYER 1",296,80,11);Text("R1 / LAYER 2",734,80,11);
  // Connector lines terminate at the corresponding physical group; each label is editable.
  void Callout(string id,double x,double y,double tx,double ty){double end=x<280?x+238:x;var line=new Polyline{Stroke=Line,StrokeThickness=1,Points=new PointCollection{new(tx,ty),new((end+tx)/2,ty),new(end,y+16)}};canvas.Children.Add(line);var b=new Button{Width=238,Height=31,Padding=new Thickness(8,3,8,3),Margin=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Left,FontSize=11};b.Click+=(_,_)=>Select(id);ShapeButton(b,x,y);controls[id]=b;}
  Callout("L2",12,24,341,104);Callout("Select",12,61,326,133);Callout("D-pad Up",12,111,333,159);Callout("D-pad Left",12,148,313,178);Callout("D-pad Right",12,185,352,178);Callout("D-pad Down",12,222,333,200);
  Callout("Left stick Up",12,271,334,261);Callout("Left stick Left",12,308,310,285);Callout("Left stick Right",12,345,358,285);Callout("Left stick Down",12,382,334,311);
  Callout("R2",870,24,780,104);Callout("Start",870,61,797,133);Callout("Y",870,111,786,158);Callout("X",870,148,761,184);Callout("B",870,185,811,184);Callout("A",870,222,786,210);
  Callout("Right stick Up",870,271,782,261);Callout("Right stick Left",870,308,756,285);Callout("Right stick Right",870,345,806,285);Callout("Right stick Down",870,382,782,311);
  Callout("L3",294,390,334,311);Callout("R3",588,390,782,311);
 }
 void ShapeButton(Button b,double x,double y){Canvas.SetLeft(b,x);Canvas.SetTop(b,y);canvas.Children.Add(b);}
 void LoadProfile(){bool old=loading;loading=true;rename.Text=Current.Name;dead.Value=Current.DeadZone*100;trigger.Value=Current.TriggerThreshold*100;speed.Value=Current.MouseSpeed;LoadModes();loading=old;Select(selected);RefreshLabels();}
 void LoadModes(){bool old=loading;loading=true;leftMode.SelectedItem=Editing.LeftStick;rightMode.SelectedItem=Editing.RightStick;loading=old;}
 void ModeChanged(){if(loading)return;Editing.LeftStick=(string?)leftMode.SelectedItem??"Keys";Editing.RightStick=(string?)rightMode.SelectedItem??"Keys";engine.Reset();Save();RefreshLabels();}
 void Select(string id){if(capturing)CancelCapture();selected=id;bindingTitle.Text=Engine.DisplayName(id)+"  /  "+new[]{"BASE","L1","R1"}[Math.Max(0,layers.SelectedIndex)];draftKeys.Clear();draftMouse.Clear();draftPadBoard=false;if(Editing.Bindings.TryGetValue(id,out var b)){draftKeys.AddRange(b.Keys);draftMouse.AddRange(b.Mouse);draftPadBoard=b.PadBoard;}ShowDraft();RefreshLabels();}
 void ShowDraft(){
  loadingBinding=true;bindingType.SelectedIndex=draftPadBoard?1:0;loadingBinding=false;
  actions.Visibility=keyActions.Visibility=mouseActions.Visibility=draftPadBoard?Visibility.Collapsed:Visibility.Visible;
  actions.ItemsSource=draftKeys.Select(k=>"KEY   "+Engine.KeyName(k)).Concat(draftMouse.Select(m=>"MOUSE   "+m)).ToArray();
  details.Text=draftPadBoard?"Press this input to open PadBoard. Press R3 in PadBoard to return to VitaPad.":"Hold the input to hold its assigned keys and mouse buttons.";
 }
 void ApplyDraft(){draftKeys.RemoveAll(k=>k<1||k>254);var b=new Binding{PadBoard=draftPadBoard,Keys=draftKeys.Distinct().Take(16).ToList(),Mouse=draftMouse.Distinct().ToList()};Editing.Bindings[selected]=b;draftKeys.Clear();draftKeys.AddRange(b.Keys);draftMouse.Clear();draftMouse.AddRange(b.Mouse);ShowDraft();Save();RefreshLabels();}
 void RefreshLabels(){foreach(var (id,b) in controls){string label=Editing.Bindings.TryGetValue(id,out var binding)&&binding.Label.Length>0?binding.Label:"Unassigned";if(id.StartsWith("Left stick")&&Editing.LeftStick!="Keys")label=Editing.LeftStick;if(id.StartsWith("Right stick")&&Editing.RightStick!="Keys")label=Editing.RightStick;b.Content=new TextBlock{Text=Engine.DisplayName(id)+"  ·  "+label,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=216};b.ToolTip=Engine.DisplayName(id)+" → "+label;b.BorderBrush=id==selected?Accent:Line;}}
 void BeginCapture(){capturing=true;captureKeys.Clear();captureButton.Content="Press chord; release to save";details.Text="Press keys together. Esc cancels; use Add key to assign Escape alone.";captureButton.Focus();}
 void CancelCapture(){capturing=false;captureKeys.Clear();captureButton.Content="Record key / chord";details.Text="Outputs are paused while this editor is open.";}
 void RecordDown(object sender,KeyEventArgs e){if(!capturing)return;e.Handled=true;var key=e.Key==Key.System?e.SystemKey:e.Key;if(key==Key.Escape){CancelCapture();return;}captureKeys.Add(KeyInterop.VirtualKeyFromKey(key));}
 void RecordUp(object sender,KeyEventArgs e){if(!capturing)return;e.Handled=true;if(captureKeys.Count==0)return; // Save once the entire physical chord is released.
  if(captureKeys.Any(k=>(NativeKeyState.GetAsyncKeyState(k)&0x8000)!=0))return;draftKeys.Clear();draftKeys.AddRange(captureKeys.OrderBy(k=>Engine.IsModifier(k)?0:1));CancelCapture();ApplyDraft();}
 void Save(){if(preview)return;try{settings.Save(dataPath);}catch(Exception e){details.Text="Save failed: "+e.Message;}}
 void Import(){var d=new Microsoft.Win32.OpenFileDialog{Filter="VitaPad settings (*.json)|*.json"};if(d.ShowDialog(this)!=true)return;try{var imported=Settings.Parse(File.ReadAllText(d.FileName));if(File.Exists(dataPath))File.Copy(dataPath,dataPath+".backup",true);imported.Save(dataPath);engine.Reset();settings=imported;loading=true;profiles.ItemsSource=settings.Profiles.Select(p=>p.Name).ToArray();profiles.SelectedIndex=settings.ActiveProfile;loading=false;LoadProfile();}catch(Exception e){System.Windows.MessageBox.Show(this,"Import failed. Current settings were kept.\n\n"+e.Message,"VitaPad");}}
 void Export(){var d=new Microsoft.Win32.SaveFileDialog{Filter="VitaPad settings (*.json)|*.json",FileName="VitaPad-settings.json"};if(d.ShowDialog(this)!=true)return;try{settings.Save(d.FileName);}catch(Exception e){System.Windows.MessageBox.Show(this,e.Message,"Export failed");}}
 internal void ShowEditor(){if(exiting)return;overlay.Hide();engine.Exit.Cancel();exitPrompt.Hide();IsEnabled=true;engine.Reset();WindowState=WindowState.Maximized;Show();Activate();}
 void Poll(){if(preview){status.Text="Preview · Xbox 360 layout";live.Text="Base + two modifier layers";return;}double now=watch.Elapsed.TotalSeconds,dt=now-last;last=now;Pad? pad=null;if(controller.HasValue && NativeOutput.XInputGetState(controller.Value,out var s)==0)pad=s.Pad;else{if(controller.HasValue){RouteController(null,0);UpdateStartShortcut(null,now);hint.Reset();}controller=null;for(uint i=0;i<4;i++)if(NativeOutput.XInputGetState(i,out var found)==0){controller=i;pad=found.Pad;break;}}
  ProcessControllerFrame(pad,now,dt);if(exiting)return;
  string state=controller.HasValue?$"Xbox controller · slot {controller.Value+1}":"Waiting for Xbox controller…";if(IsVisible){status.Text=state;live.Text=pad.HasValue?"Live layer: "+new[]{"Base","L1","R1"}[engine.LayerIndex]:"Connect Moonlight to begin";
   foreach(var (id,b) in controls)b.Background=engine.Active.Contains(id)?Brush("#58373E"):Brush("#292E33");foreach(var (id,shape) in indicators)shape.Stroke=engine.Active.Contains(id)?White:Accent;
  }
  string trayText="VitaPad · "+(paused?"paused":IsVisible?"editing (paused)":padBoardOpen?"PadBoard · R3 to return":controller.HasValue?Current.Name:"waiting for controller");if(native.Failed)trayText="VitaPad · Windows blocked an output";if(tray.Text!=trayText)tray.Text=trayText.Length>63?trayText[..63]:trayText;
 }
 void ProcessControllerFrame(Pad? pad,double now,double dt){
  RouteController(pad,dt);
  if(exiting)return;
  if(engine.ExitRequested){Exit();return;}
  UpdateStartShortcut(pad,now);
  SyncPopups(referenceState,now);
  if(IsVisible){
   string? detected=Engine.Ids.FirstOrDefault(engine.Active.Contains);
   if(detected!=null&&detected!=lastDetected&&!capturing)Select(detected);
   lastDetected=detected;
  }
 }
 void UpdateStartShortcut(Pad? pad,double now){
  const ushort startButton=0x0010;
  bool held=(pad?.Buttons&startButton)==startButton;
  if(!held||!IsVisible||engine.Exit.IsOpen||engine.Exit.JustClosed){
   startHoldBegan=null;
   startNeedsRelease=held||!pad.HasValue;
   return;
  }
  if(startNeedsRelease)return;
  startHoldBegan??=now;
  if(now<startHoldBegan.Value+1)return;
  startHoldBegan=null;
  startNeedsRelease=true;
  Hide();
 }
 void SyncPopups(Pad? pad,double now){
  if(engine.Exit.IsOpen){
   hint.Update(pad?.Buttons??0,true,now);overlay.Hide();IsEnabled=false;
   if(!exitPrompt.IsVisible)exitPrompt.Show();
  }else{
   if(exitPrompt.IsVisible)exitPrompt.Hide();IsEnabled=true;
   if(IsVisible||paused||padBoardOpen||pad is not Pad state){hint.Reset();overlay.Hide();return;}
   var layer=Current.Layers[engine.LayerIndex];
   bool used=(state.Buttons&0x300)!=0&&HintGate.HasActiveBinding(layer,engine.Active);
   if(hint.Update(state.Buttons,used,now))
    overlay.ShowLayer(Current,engine.LayerIndex,(state.Buttons&0x300)==0x300,engine.Active);
   else overlay.Hide();
  }
 }
 void Exit(){if(exiting)return;exiting=true;Cleanup();WApplication.Current.Shutdown();}
 bool cleaned;
 public void Cleanup(){if(cleaned)return;cleaned=true;timer.Stop();controllerClock?.Dispose();engine.Reset();padBoard?.Close();overlay.Close();exitPrompt.AllowClose=true;exitPrompt.Close();tray.Visible=false;tray.Dispose();}
}
static class NativeKeyState { [System.Runtime.InteropServices.DllImport("user32.dll")]public static extern short GetAsyncKeyState(int key); }
