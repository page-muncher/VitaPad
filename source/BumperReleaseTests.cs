namespace VitaPad;

internal static class BumperReleaseTests
{
    sealed class Output : IOutput
    {
        public List<string> Events {get;} = [];
        public void Key(int key,bool down)=>Events.Add($"key:{key}:{down}");
        public void Mouse(string action,bool down)=>Events.Add($"mouse:{action}:{down}");
        public void Move(int x,int y){if(x!=0||y!=0)Events.Add("pointer");}
        public void Wheel(int vertical,int horizontal){if(vertical!=0||horizontal!=0)Events.Add("wheel");}
    }

    public static void Run(Action<bool,string> check)
    {
        var output = new Output();
        var engine = new Engine(output);
        var profile = new Profile();
        profile.Layers[0].Bindings["R2"] = new Binding{Mouse=["Left click"]};
        profile.Layers[1].Bindings["R2"] = new Binding{Keys=[18,76]};
        profile.Layers[1].Bindings["Y"] = new Binding{Keys=[84]};
        profile.Layers[2].Bindings["R2"] = new Binding{Mouse=["Right click"]};
        profile.Layers[2].Bindings["Y"] = new Binding{Keys=[89]};
        const ushort L1=0x100,R1=0x200,Both=0x300,Triangle=0x8000;
        void Tick(ushort buttons=0,byte rt=0,short lx=0,short ly=0)
        {
            output.Events.Clear();
            engine.Tick(new Pad{Buttons=buttons,RT=rt,LX=lx,LY=ly},profile,false,.05);
        }

        Tick(); Tick(L1); Tick(L1,255);
        check(output.Events.SequenceEqual(new[]{"key:18:True","key:76:True"}),"L1 plus R2 presses Alt+L");
        Tick(0,255);
        check(output.Events.SequenceEqual(new[]{"key:76:False","key:18:False"}),"Releasing L1 first ends Alt+L without pressing the base mouse button");
        Tick(0,255); check(output.Events.Count==0,"Held R2 stays blocked in base across frames");
        Tick(); check(output.Events.Count==0,"Releasing blocked R2 emits no click or extra key-up");
        Tick(0,255); check(output.Events.SequenceEqual(new[]{"mouse:Left click:True"}),"Fresh R2 in base still clicks normally");
        Tick();

        Tick(L1); Tick(L1,255); Tick((ushort)(L1|Triangle));
        check(output.Events.SequenceEqual(new[]{"key:76:False","key:18:False","key:84:True"}),"R2 release and fresh L1 plus Triangle work in the same frame without lifting L1");
        Tick(L1); Tick(L1,255);
        check(output.Events.SequenceEqual(new[]{"key:18:True","key:76:True"}),"R2 can be used again while the same L1 hold continues");
        Tick(); check(output.Events.SequenceEqual(new[]{"key:76:False","key:18:False"}),"Releasing both chord controls in the same frame only releases Alt+L");

        Tick(R1); Tick(R1,255); Tick(0,255);
        check(output.Events.SequenceEqual(new[]{"mouse:Right click:False"}),"Releasing R1 releases its output without firing base R2");
        Tick(); Tick(Both); Tick(Both,255); Tick((ushort)(R1|Triangle),255);
        check(output.Events.SequenceEqual(new[]{"mouse:Left click:False","key:89:True"}),"Dropping one of both bumpers blocks held R2 but allows fresh Triangle in R1");
        Tick(Both,255);
        check(output.Events.All(x=>!x.StartsWith("mouse:")),"Changing layers again does not rearm blocked R2");
        Tick(); Tick(Both); Tick(Both,255); Tick(0,255);
        check(output.Events.SequenceEqual(new[]{"mouse:Left click:False"}),"Dropping both bumpers releases held R2 even when the effective layer remains base");
        Tick();

        profile.Layers[0].LeftStick="Pointer";
        profile.Layers[1].Bindings["Left stick Right"]=new Binding{Keys=[68]};
        Tick(L1); Tick(L1,lx:30000);
        check(output.Events.Contains("key:68:True"),"Modified stick direction works normally");
        Tick(lx:30000); check(output.Events.SequenceEqual(new[]{"key:68:False"}),"Releasing L1 with stick deflected does not start base pointer motion");
        Tick(ly:30000); check(output.Events.Count==0,"Rolling a blocked stick between directions does not rearm it");
        Tick(); Tick(lx:30000); check(output.Events.SequenceEqual(new[]{"pointer"}),"Centering the stick rearms its next base movement");
    }
}
