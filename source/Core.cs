using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;

namespace VitaPad;
public sealed class Binding {
 public List<int> Keys {get;set;} = [];
 public List<string> Mouse {get;set;} = [];
 [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingDefault)]
 public bool PadBoard {get;set;}
 public string Label => PadBoard ? "PadBoard" : string.Join(" + ", Keys.Select(Engine.KeyName).Concat(Mouse));
}
public sealed class Layer { public Dictionary<string,Binding> Bindings {get;set;} = []; public string LeftStick {get;set;}="Keys"; public string RightStick {get;set;}="Keys"; }
public sealed class Profile { public string Name {get;set;}="Profile"; public List<Layer> Layers {get;set;}=[new(),new(),new()]; public double DeadZone {get;set;}=.22; public double TriggerThreshold {get;set;}=.3; public double MouseSpeed {get;set;}=900; }
public sealed class Settings {
 public static string ExecutableDirectory => Path.GetDirectoryName(Environment.ProcessPath)!;
 public int SchemaVersion {get;set;}=1; public int ActiveProfile {get;set;} public List<Profile> Profiles {get;set;}=Enumerable.Range(1,3).Select(i=>new Profile{Name=$"Profile {i}"}).ToList();
 public static readonly JsonSerializerOptions Json = new(){WriteIndented=true};
 public static Settings Parse(string text) { var s=JsonSerializer.Deserialize<Settings>(text) ?? throw new InvalidDataException("Empty settings."); if(s.SchemaVersion!=1 || s.Profiles==null || s.Profiles.Count!=3 || s.ActiveProfile<0 || s.ActiveProfile>2) throw new InvalidDataException("Expected VitaPad schema 1 and exactly three profiles."); foreach(var p in s.Profiles) { if(p==null || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length>40 || p.Layers==null || p.Layers.Count!=3 || !double.IsFinite(p.DeadZone) || p.DeadZone<.05 || p.DeadZone>.9 || !double.IsFinite(p.TriggerThreshold) || p.TriggerThreshold<.05 || p.TriggerThreshold>.95 || !double.IsFinite(p.MouseSpeed) || p.MouseSpeed<50 || p.MouseSpeed>4000) throw new InvalidDataException("Invalid profile or sensitivity."); foreach(var l in p.Layers) { if(l==null || l.Bindings==null || !new[]{"Keys","Pointer","Scroll"}.Contains(l.LeftStick) || !new[]{"Keys","Pointer","Scroll"}.Contains(l.RightStick)) throw new InvalidDataException("Invalid layer."); foreach(var (id,b) in l.Bindings) if(!Engine.Ids.Contains(id) || b==null || b.Keys==null || b.Mouse==null || b.Keys.Count>16 || b.Keys.Any(k=>k<1||k>254) || b.Mouse.Any(m=>!Engine.MouseActions.Contains(m))) throw new InvalidDataException("Invalid binding."); } } return s; }
 public void Save(string path) { var temp=path+".tmp"; File.WriteAllText(temp,JsonSerializer.Serialize(this,Json)); File.Move(temp,path,true); }
}
[StructLayout(LayoutKind.Sequential)] public struct Pad { public ushort Buttons; public byte LT,RT; public short LX,LY,RX,RY; }
[StructLayout(LayoutKind.Sequential)] public struct PadState { public uint Packet; public Pad Pad; }
public interface IOutput {
 void Key(int key,bool down);
 void Keys(IReadOnlyList<int> keys,bool down) { foreach(int key in keys) Key(key,down); }
 void Mouse(string action,bool down);
 void Move(int x,int y);
 void Wheel(int vertical,int horizontal);
}
public sealed class NativeOutput : IOutput {
 [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int dx,dy; public uint data,flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
 [StructLayout(LayoutKind.Explicit)] internal struct Union { [FieldOffset(0)] public MouseInput mouse; [FieldOffset(0)] public KeyInput key; }
 [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint type; public Union value; }
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,[In] Input[] inputs,int size);
 [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint index,out PadState state);
 static readonly int InputSize=Marshal.SizeOf<Input>();
 readonly Func<uint,Input[],int,uint> sendInput;
 public NativeOutput() : this(SendInput) { }
 internal NativeOutput(Func<uint,Input[],int,uint> sendInput) => this.sendInput=sendInput;
 public bool Failed {get;private set;}
 void SendBatch(Input[] events) { uint count=(uint)events.Length; if(count!=0 && sendInput(count,events,InputSize)!=count) Failed=true; }
 void Send(Input i) => SendBatch([i]);
 static Input Keyboard(int k,bool down) {
  uint flags=down?0u:2u;
  if(k is 33 or 34 or 35 or 36 or 37 or 38 or 39 or 40 or 45 or 46 or 91 or 92 or 111 or 144 or 163 or 165) flags|=1;
  return new(){type=1,value=new(){key=new(){vk=(ushort)k,flags=flags}}};
 }
 public void Key(int k,bool down) => Send(Keyboard(k,down));
 // Windows enqueues every event in one SendInput batch without interleaving other input.
 public void Keys(IReadOnlyList<int> keys,bool down) {
  if(keys.Count==0)return;
  var events=new Input[keys.Count];
  for(int i=0;i<events.Length;i++)events[i]=Keyboard(keys[i],down);
  SendBatch(events);
 }
 public void Mouse(string a,bool down) { uint f=a switch {"Left click"=>down?2u:4u,"Right click"=>down?8u:16u,"Middle click"=>down?32u:64u,_=>down?128u:256u}; Send(new(){value=new(){mouse=new(){flags=f,data=a=="Mouse 4"?1u:a=="Mouse 5"?2u:0u}}}); }
 public void Move(int x,int y) { if(x!=0||y!=0) Send(new(){value=new(){mouse=new(){dx=x,dy=y,flags=1}}}); }
 public void Wheel(int v,int h) { if(v!=0) Send(new(){value=new(){mouse=new(){data=unchecked((uint)(v*120)),flags=0x800}}}); if(h!=0) Send(new(){value=new(){mouse=new(){data=unchecked((uint)(h*120)),flags=0x1000}}}); }
}
public sealed class Engine(IOutput output) {
 public static string DisplayName(string id)=>id switch { "A"=>"×", "B"=>"○", "X"=>"□", "Y"=>"△", _=>id };
 public static string KeyName(int k)=>k switch {8=>"Backspace",9=>"Tab",13=>"Enter",16=>"Shift",17=>"Ctrl",18=>"Alt",20=>"Caps Lock",27=>"Escape",32=>"Space",33=>"Page Up",34=>"Page Down",37=>"Left arrow",38=>"Up arrow",39=>"Right arrow",40=>"Down arrow",44=>"Print Screen",91=>"Left Win",92=>"Right Win",93=>"Menu",144=>"Num Lock",145=>"Scroll Lock",160=>"Left Shift",161=>"Right Shift",162=>"Left Ctrl",163=>"Right Ctrl",164=>"Left Alt",165=>"Right Alt",186=>"; / :",187=>"= / +",188=>", / <",189=>"- / _",190=>". / >",191=>"/ / ?",192=>"` / ~",219=>"[ / {",220=>"\\ / |",221=>"] / }",222=>"' / quote",>=48 and <=57=>((char)k).ToString(),>=96 and <=105=>"Numpad "+(k-96),_=>((System.Windows.Forms.Keys)k).ToString()};
 public static readonly string[] Ids=["A","B","X","Y","Start","Select","L3","R3","L2","R2","D-pad Up","D-pad Down","D-pad Left","D-pad Right","Left stick Up","Left stick Down","Left stick Left","Left stick Right","Right stick Up","Right stick Down","Right stick Left","Right stick Right"];
 public static readonly string[] MouseActions=["Left click","Right click","Middle click","Mouse 4","Mouse 5","Wheel Up","Wheel Down","Wheel Left","Wheel Right"];
 readonly List<int> heldKeys=[]; readonly HashSet<string> heldMouse=[]; readonly Dictionary<int,double> repeats=[]; readonly Dictionary<string,double> wheels=[];
 bool neutralRequired=true; int previousLayer=-1; Profile? previousProfile; double mx,my,sx,sy;
 const ushort BumperMask=0x300;
 ushort previousBumpers;
 IReadOnlySet<string> previousControls=emptyControls;
 readonly HashSet<string> blockedUntilRelease=[];
 bool leftStickBlocked,rightStickBlocked;
 public ExitFlow Exit {get;} = new();
 public bool ExitRequested => Exit.Confirmed;
 public bool PadBoardRequested {get;private set;}
 public int LayerIndex {get;private set;} public HashSet<string> Active {get;private set;}=[];
 public static int GetLayer(Pad p) => (p.Buttons&0x300)==0x300?0:(p.Buttons&0x100)!=0?1:(p.Buttons&0x200)!=0?2:0;
 public static HashSet<string> Decode(Pad p,Profile profile) { HashSet<string> a=[]; (string,int)[] masks=[("A",0x1000),("B",0x2000),("X",0x4000),("Y",0x8000),("Start",16),("Select",32),("L3",64),("R3",128),("D-pad Up",1),("D-pad Down",2),("D-pad Left",4),("D-pad Right",8)]; foreach(var (id,mask) in masks) if((p.Buttons&mask)!=0)a.Add(id); if(p.LT/255.0>=profile.TriggerThreshold)a.Add("L2"); if(p.RT/255.0>=profile.TriggerThreshold)a.Add("R2"); void Stick(string name,short x,short y) { if(x/32768.0>profile.DeadZone)a.Add(name+" Right"); if(x/32768.0< -profile.DeadZone)a.Add(name+" Left"); if(y/32768.0>profile.DeadZone)a.Add(name+" Up"); if(y/32768.0< -profile.DeadZone)a.Add(name+" Down"); } Stick("Left stick",p.LX,p.LY); Stick("Right stick",p.RX,p.RY); return a; }
 // Release ordinary keys first, even if a separate binding added a modifier later.
 int[] KeysToRelease(Func<int,bool> shouldRelease) => heldKeys.AsEnumerable().Reverse().Where(shouldRelease).OrderBy(IsModifier).ToArray();
 public void Release() { var released=KeysToRelease(_=>true); if(released.Length!=0)output.Keys(released,false); foreach(var m in heldMouse)output.Mouse(m,false); heldKeys.Clear(); heldMouse.Clear(); repeats.Clear(); wheels.Clear(); mx=my=sx=sy=0; }
 public void Reset() { Release(); neutralRequired=true; previousBumpers=0; previousControls=emptyControls; blockedUntilRelease.Clear(); leftStickBlocked=rightStickBlocked=false; }
 public void ResumeAfterPadBoard(Profile profile) {
  Reset();
  previousProfile=profile;
  // The handoff filters individual controls held when PadBoard closes.
  neutralRequired=false;
 }
 static readonly IReadOnlySet<string> emptyControls=new HashSet<string>();
 static bool LeftDirection(string id)=>id.StartsWith("Left stick ",StringComparison.Ordinal);
 static bool RightDirection(string id)=>id.StartsWith("Right stick ",StringComparison.Ordinal);
 void GuardBumperRelease(ushort bumpers) {
  blockedUntilRelease.IntersectWith(Active);
  bool leftMoved=Active.Any(LeftDirection),rightMoved=Active.Any(RightDirection);
  leftStickBlocked &= leftMoved; rightStickBlocked &= rightMoved;
  // Releasing either bumper must not reinterpret a control that is already down.
  // Track the physical bumper pattern even when both bumpers and neither use base.
  if((previousBumpers & ~bumpers)!=0){
   foreach(var id in Active)
    if(!LeftDirection(id)&&!RightDirection(id)&&previousControls.Contains(id))blockedUntilRelease.Add(id);
   leftStickBlocked |= leftMoved && previousControls.Any(LeftDirection);
   rightStickBlocked |= rightMoved && previousControls.Any(RightDirection);
   mx=my=sx=sy=0;
  }
  previousBumpers=bumpers; previousControls=Active;
 }
 public void Tick(Pad? state,Profile profile,bool paused,double dt) {
  PadBoardRequested=false;
  dt=Math.Clamp(dt,0,.05); Exit.Update(state);
  if(state is not Pad p){Reset();Active=[];return;} Active=Decode(p,profile); LayerIndex=GetLayer(p);
  if(Exit.IsOpen || Exit.Confirmed || Exit.JustClosed){Reset();return;}
  if(paused){Reset();return;}
  if(previousProfile!=profile){Reset(); previousProfile=profile;} if(previousLayer!=LayerIndex){Release(); previousLayer=LayerIndex;}
  if(neutralRequired){if(Active.Count==0)neutralRequired=false; else return;}
  var previouslyActive=previousControls;
  GuardBumperRelease((ushort)(p.Buttons&BumperMask));
  var layer=profile.Layers[LayerIndex]; List<int> keys=[]; HashSet<string> mouse=[]; HashSet<string> wheel=[];
  foreach(var id in Ids.Where(Active.Contains)) {
   if(blockedUntilRelease.Contains(id))continue;
   if(id=="R3" && (p.Buttons&BumperMask)==BumperMask)continue;
   if(LeftDirection(id)&&(leftStickBlocked||layer.LeftStick!="Keys") || RightDirection(id)&&(rightStickBlocked||layer.RightStick!="Keys"))continue;
   if(!layer.Bindings.TryGetValue(id,out var b))continue;
   if(b.PadBoard){
    if(previouslyActive.Contains(id))continue;
    PadBoardRequested=true;
    Reset();
    return;
   }
   foreach(var k in b.Keys)if(!keys.Contains(k))keys.Add(k);
   foreach(var m in b.Mouse)if(m.StartsWith("Wheel"))wheel.Add(m);else mouse.Add(m);
  }
  var released=KeysToRelease(k=>!keys.Contains(k));
  if(released.Length!=0)output.Keys(released,false);
  foreach(var k in released){heldKeys.Remove(k);repeats.Remove(k);}
  // Modifiers always go down before ordinary keys, regardless of chord entry order.
  var pressed=keys.Where(k=>!heldKeys.Contains(k)).OrderBy(k=>!IsModifier(k)).ToArray();
  if(pressed.Length!=0)output.Keys(pressed,true);
  foreach(var k in pressed){heldKeys.Add(k);repeats[k]=-.45;}
  foreach(var m in heldMouse.Except(mouse).ToArray()){output.Mouse(m,false);heldMouse.Remove(m);} foreach(var m in mouse.Except(heldMouse).ToArray()){output.Mouse(m,true);heldMouse.Add(m);}
  foreach(var k in heldKeys.Where(k=>!IsModifier(k)).ToArray()){repeats[k]+=dt;if(repeats[k]>=0){output.Key(k,true);repeats[k]-=.033;}}
  foreach(var m in wheels.Keys.Except(wheel).ToArray())wheels.Remove(m); foreach(var m in wheel){ if(!wheels.ContainsKey(m))wheels[m]=0; wheels[m]-=dt;if(wheels[m]<=0){output.Wheel(m=="Wheel Up"?1:m=="Wheel Down"?-1:0,m=="Wheel Right"?1:m=="Wheel Left"?-1:0);wheels[m]+=.09;} }
  double Axis(short value){double v=value/32768.0; return Math.Abs(v)<=profile.DeadZone?0:Math.Sign(v)*(Math.Abs(v)-profile.DeadZone)/(1-profile.DeadZone);}
  void Motion(string mode,short x,short y){double ax=Axis(x),ay=Axis(y);if(mode=="Pointer"){mx+=ax*profile.MouseSpeed*dt;my-=ay*profile.MouseSpeed*dt;}if(mode=="Scroll"){sx+=ax*12*dt;sy+=ay*12*dt;}}
  if(!leftStickBlocked)Motion(layer.LeftStick,p.LX,p.LY); if(!rightStickBlocked)Motion(layer.RightStick,p.RX,p.RY); int dx=(int)mx,dy=(int)my;output.Move(dx,dy);mx-=dx;my-=dy;int sv=(int)sy,sh=(int)sx;output.Wheel(sv,sh);sy-=sv;sx-=sh;
 }
 public static bool IsModifier(int k)=>k is 16 or 17 or 18 or 91 or 92 or >=160 and <=165;
}
