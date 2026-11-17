using System.IO;
using System.Text.Json;
namespace VitaPad;
public static class Tests {
 sealed class Recorder:IOutput { public readonly List<string> Events=[];public void Key(int k,bool d)=>Events.Add($"K{k}:{d}");public void Mouse(string m,bool d)=>Events.Add($"M{m}:{d}");public void Move(int x,int y){if(x!=0||y!=0)Events.Add($"P{x},{y}");}public void Wheel(int v,int h){if(v!=0||h!=0)Events.Add($"W{v},{h}");} }
 public static int Run(string path){int count=0;var lines=new List<string>();try{
 void Check(bool pass,string name){if(!pass)throw new Exception(name);lines.Add("PASS "+name);count++;}
 var r=new Recorder();var e=new Engine(r);var p=new Profile();p.Layers[0].Bindings["A"]=new(){Keys=[17,83]};p.Layers[0].Bindings["B"]=new(){Keys=[83]};p.Layers[1].Bindings["A"]=new(){Keys=[65]};p.Layers[2].Bindings["A"]=new(){Keys=[66]};
 void Tick(ushort buttons=0,double dt=.008)=>e.Tick(new Pad{Buttons=buttons},p,false,dt);
 Tick(0x1000);Check(r.Events.Count==0,"Startup waits for neutral");Tick();Tick(0x1000);Check(r.Events.SequenceEqual(new[]{"K17:True","K83:True"}),"Chord modifier precedes key");r.Events.Clear();Tick(0x3000);Tick(0x2000);Check(!r.Events.Contains("K83:False"),"Shared key remains held while another owner holds");Tick();Check(r.Events.Contains("K83:False")&&r.Events.Contains("K17:False"),"Chord and shared output release");
 r.Events.Clear();Tick(0x1000);r.Events.Clear();Tick(0x1100);Check(r.Events.Contains("K83:False")&&r.Events.Contains("K65:True"),"L1 switches to layer 1 and releases base");r.Events.Clear();Tick(0x1200);Check(r.Events.Contains("K65:False")&&!r.Events.Contains("K66:True"),"Switching bumpers releases old output without transferring held Cross");Tick(0x200);r.Events.Clear();Tick(0x1200);Check(r.Events.Contains("K66:True"),"Fresh Cross uses R1 layer");r.Events.Clear();Tick(0x1300);Check(r.Events.Contains("K66:False")&&r.Events.Contains("K83:True"),"Both bumpers release alternate output and select base");r.Events.Clear();Tick(0x1000);Check(r.Events.SequenceEqual(new[]{"K83:False","K17:False"}),"Releasing both bumpers ends the held base output without re-triggering it");Tick();Tick(0x1000);r.Events.Clear();e.Tick(null,p,false,.008);Check(r.Events.Contains("K83:False"),"Disconnect releases outputs");r.Events.Clear();Tick(0x1000);Check(r.Events.Count==0,"Reconnect with held input is guarded");Tick();Tick(0x1000);r.Events.Clear();e.Tick(new Pad{Buttons=0x1000},p,true,.008);Check(r.Events.Contains("K83:False"),"Editor pause releases held outputs");
 Tick();r.Events.Clear();Tick(0x1000);r.Events.Clear();for(int i=0;i<70;i++)Tick(0x1000,.01);Check(r.Events.Contains("K83:True")&&!r.Events.Contains("K17:True"),"Held keys repeat without repeating modifiers");Tick();
 Check(Engine.Decode(new Pad{LT=75},p).Count==0&&Engine.Decode(new Pad{LT=100},p).Contains("L2"),"Analog trigger threshold");Check(Engine.Decode(new Pad{LX=3000},p).Count==0&&Engine.Decode(new Pad{LX=20000,LY=-20000},p).SetEquals(new[]{"Left stick Right","Left stick Down"}),"Stick dead zone and diagonal directions");
 p.Layers[0].LeftStick="Pointer";r.Events.Clear();e.Tick(new Pad{LX=32767},p,false,.02);Check(r.Events.Any(x=>x.StartsWith("P")),"Analog pointer motion");p.Layers[0].LeftStick="Scroll";r.Events.Clear();for(int i=0;i<12;i++)e.Tick(new Pad{LY=32767},p,false,.02);Check(r.Events.Any(x=>x.StartsWith("W1")),"Analog scrolling");p.Layers[0].LeftStick="Keys";
 p.Layers[0].Bindings["X"]=new(){Mouse=["Left click","Wheel Down"]};r.Events.Clear();Tick(0x4000);Check(r.Events.Contains("MLeft click:True")&&r.Events.Contains("W-1,0"),"Mouse click and wheel binding");r.Events.Clear();Tick();Check(r.Events.Contains("MLeft click:False"),"Mouse release");
 var settings=new Settings();settings.Profiles[0]=p;var json=JsonSerializer.Serialize(settings,Settings.Json);var roundtrip=Settings.Parse(json);Check(roundtrip.Profiles[0].Layers[1].Bindings["A"].Keys[0]==65,"Export/import preserves all layers");
 bool rejects=false;try{Settings.Parse(json.Replace("\"SchemaVersion\": 1","\"SchemaVersion\": 99"));}catch{rejects=true;}Check(rejects,"Future schema rejects without overwriting");rejects=false;try{Settings.Parse("{}");}catch{rejects=true;}Check(!rejects,"Default blank configuration is valid");
 var tmp=Path.Combine(Path.GetTempPath(),"vitapad-test-"+Guid.NewGuid()+".json");try{settings.Save(tmp);Check(Settings.Parse(File.ReadAllText(tmp)).Profiles.Count==3,"Atomic persistence roundtrip");}finally{File.Delete(tmp);}
 var adjacent=Path.Combine(Settings.ExecutableDirectory,".vitapad-test-"+Guid.NewGuid()+".json");try{settings.Save(adjacent);Check(Settings.Parse(File.ReadAllText(adjacent)).Profiles.Count==3,"Portable settings write beside actual executable");}finally{File.Delete(adjacent);}
 bool Invalid(Settings candidate){try{Settings.Parse(JsonSerializer.Serialize(candidate));return false;}catch{return true;}}
 var bad=new Settings();bad.Profiles[0].Layers.RemoveAt(0);Check(Invalid(bad),"Import rejects missing layers");bad=new();bad.Profiles[0].Layers[0].Bindings["A"]=new(){Keys=[999]};Check(Invalid(bad),"Import rejects invalid key codes");bad=new();bad.Profiles[0].Layers[0].Bindings["A"]=new(){Mouse=["bad action"]};Check(Invalid(bad),"Import rejects unknown mouse outputs");bad=new();bad.Profiles[0].DeadZone=1;Check(Invalid(bad),"Import rejects invalid sensitivity");
 var blank=new Settings();Check(blank.Profiles.Count==3&&blank.Profiles.All(x=>x.Layers.Count==3&&x.Layers.All(l=>l.Bindings.Count==0)),"All three profiles and nine layers start blank");
 Tick();Tick(0x1000);r.Events.Clear();var next=new Profile();e.Tick(new Pad{Buttons=0x1000},next,false,.008);Check(r.Events.Contains("K83:False")&&!r.Events.Any(x=>x.EndsWith("True")),"Changing profiles releases output and waits for neutral");
 r.Events.Clear();Tick(0x380);Check(e.Exit.IsOpen&&!e.ExitRequested&&!r.Events.Any(x=>x.EndsWith("True")),"Three-button chord opens exit confirmation");Tick();Tick(0x1000);Check(e.ExitRequested,"Cross confirms requested exit");
 ExitFlowTests.Run(Check);
 OverlayTests.Run(Check);
 HintGateTests.Run(Check);
 KeyBatchTests.Run(Check);
 BumperReleaseTests.Run(Check);
 PadBoardTests.Run(Check);
 ControllerKeyboard.ModifierTests.Run(Check);
 lines.Add($"PASS: {count} assertions");File.WriteAllLines(path,lines);return 0;
 }catch(Exception ex){lines.Add("FAIL "+ex);File.WriteAllLines(path,lines);return 1;}}
}
