using System.IO;
using System.Media;
using System.Runtime.InteropServices;

namespace ControllerKeyboard;

internal sealed unsafe class KeyboardSounds : IDisposable
{
    private const int Rate = 48000;
    private const int VoiceCount = 4;
    private const int CreateSourceVoiceSlot = 5;
    private const int CreateMasteringVoiceSlot = 7;
    private const int DestroyVoiceSlot = 18;
    private const int StartVoiceSlot = 19;
    private const int SubmitSourceBufferSlot = 21;
    private const int GetStateSlot = 25;
    private const uint EndOfStream = 0x40;
    private const uint SkipSampleCount = 0x0100;

    private readonly Clip key = new(0.027, 0.21, 0.004, 1750);
    private readonly Clip backspace = new(0.038, 0.43, 0.006, 1060);
    private readonly Clip toggle = new(0.059, 0.32, 0.013, 260);
    private readonly List<nint> voices = new(VoiceCount);
    private nint engine;
    private nint mastering;

    public KeyboardSounds()
    {
        try
        {
            Open();
        }
        catch (DllNotFoundException)
        {
            CloseEngine();
        }
        catch (EntryPointNotFoundException)
        {
            CloseEngine();
        }
    }

    public void Key() => Play(key);
    public void Backspace() => Play(backspace);
    public void Toggle() => Play(toggle);

    private void Open()
    {
        if (XAudio2Create(out engine, 0, 0) < 0)
            return;

        var createMastering = (delegate* unmanaged[Stdcall]<nint, nint*, uint, uint, uint, nint, nint, int, int>)
            Method(engine, CreateMasteringVoiceSlot);
        nint output = 0;
        if (createMastering(engine, &output, 0, 0, 0, 0, 0, 0) < 0)
        {
            CloseEngine();
            return;
        }
        mastering = output;

        var format = new WaveFormat
        {
            FormatTag = 1, Channels = 1, SamplesPerSecond = Rate, BytesPerSecond = Rate * 2, BlockAlign = 2, BitsPerSample = 16
        };
        var createSource = (delegate* unmanaged[Stdcall]<nint, nint*, WaveFormat*, uint, float, nint, nint, nint, int>)
            Method(engine, CreateSourceVoiceSlot);
        for (int i = 0; i < VoiceCount; i++)
        {
            nint voice = 0;
            if (createSource(engine, &voice, &format, 0, 1f, 0, 0, 0) < 0)
                break;

            var start = (delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Method(voice, StartVoiceSlot);
            if (start(voice, 0, 0) < 0)
            {
                Destroy(voice);
                break;
            }
            voices.Add(voice);
        }
        if (voices.Count == 0)
            CloseEngine();
    }

    private void Play(Clip clip)
    {
        if (voices.Count == 0)
        {
            clip.PlayFallback();
            return;
        }

        foreach (nint voice in voices)
        {
            var getState = (delegate* unmanaged[Stdcall]<nint, VoiceState*, uint, void>)Method(voice, GetStateSlot);
            VoiceState state = default;
            getState(voice, &state, SkipSampleCount);
            if (state.BuffersQueued != 0)
                continue;

            var submit = (delegate* unmanaged[Stdcall]<nint, AudioBuffer*, nint, int>)Method(voice, SubmitSourceBufferSlot);
            AudioBuffer buffer = clip.Buffer;
            if (submit(voice, &buffer, 0) < 0)
                clip.PlayFallback();
            return;
        }
        // An already busy pool drops a click instead of playing it late.
    }

    private static nint Method(nint instance, int slot) => (*(nint**)instance)[slot];

    private static void Destroy(nint voice)
    {
        var destroy = (delegate* unmanaged[Stdcall]<nint, void>)Method(voice, DestroyVoiceSlot);
        destroy(voice);
    }

    private void CloseEngine()
    {
        foreach (nint voice in voices)
            Destroy(voice);
        voices.Clear();

        if (mastering != 0)
        {
            Destroy(mastering);
            mastering = 0;
        }
        if (engine != 0)
        {
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)Method(engine, 2);
            release(engine);
            engine = 0;
        }
    }

    public void Dispose()
    {
        CloseEngine();
        key.Dispose();
        backspace.Dispose();
        toggle.Dispose();
    }

    private sealed class Clip : IDisposable
    {
        private readonly short[] samples;
        private SoundPlayer? fallback;
        public AudioBuffer Buffer { get; }

        public Clip(double duration, double volume, double decay, double pitch)
        {
            samples = GC.AllocateUninitializedArray<short>((int)(Rate * duration), pinned: true);
            var random = new Random(21);
            double filtered = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double t = (double)i / Rate;
                double noise = random.NextDouble() * 2 - 1;
                filtered = 0.66 * filtered + 0.34 * noise;
                double envelope = Math.Min(1, t / 0.00035) * Math.Exp(-t / decay);
                double strike = 0.58 * filtered + 0.42 * Math.Sin(t * pitch * 2 * Math.PI);
                samples[i] = (short)(short.MaxValue * volume * envelope * strike);
            }

            fixed (short* data = samples)
                Buffer = new AudioBuffer { Flags = EndOfStream, Bytes = (uint)(samples.Length * 2), Data = (nint)data };
        }

        public void PlayFallback()
        {
            try
            {
                fallback ??= CreateFallback();
                fallback.Play();
            }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
        }

        private SoundPlayer CreateFallback()
        {
            var stream = new MemoryStream(44 + samples.Length * 2);
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
            {
                writer.Write("RIFF"u8);
                writer.Write(36 + samples.Length * 2);
                writer.Write("WAVEfmt "u8);
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(Rate);
                writer.Write(Rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write("data"u8);
                writer.Write(samples.Length * 2);
                foreach (short sample in samples)
                    writer.Write(sample);
            }
            stream.Position = 0;
            return new SoundPlayer(stream);
        }

        public void Dispose()
        {
            fallback?.Stop();
            fallback?.Stream?.Dispose();
            fallback?.Dispose();
            fallback = null;
        }
    }

    // The XAudio2 SDK declares its buffer and state structs with one-byte packing.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct AudioBuffer
    {
        public uint Flags, Bytes;
        public nint Data;
        public uint PlayBegin, PlayLength, LoopBegin, LoopLength, LoopCount;
        public nint Context;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct VoiceState
    {
        public nint CurrentContext;
        public uint BuffersQueued;
        public ulong SamplesPlayed;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormat
    {
        public ushort FormatTag, Channels;
        public uint SamplesPerSecond, BytesPerSecond;
        public ushort BlockAlign, BitsPerSample, ExtraSize;
    }

    [DllImport("xaudio2_9.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int XAudio2Create(out nint engine, uint flags, uint processor);
}

