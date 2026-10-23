using System.Runtime.InteropServices;

namespace Mandarine.App.Services;

/// <summary>
/// Short, soft interface sounds synthesized at first use (no audio files to ship or license) and played
/// asynchronously by Windows, so they never delay the UI. A few kilobytes, kept outside the managed heap
/// because Windows reads them while the sound plays.
/// </summary>
internal static class UiSounds
{
    private const int SampleRate = 44_100;
    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;

    private static readonly Lazy<IntPtr> TickSound = new(() => Render(Tick));
    private static readonly Lazy<IntPtr> OpenSound = new(() => Render(Open));
    private static readonly Lazy<IntPtr> SuccessSound = new(() => Render(Success));
    private static readonly Lazy<IntPtr> ErrorSound = new(() => Render(Failure));
    private static long _lastTick;

    public static bool Enabled { get; set; } = true;

    /// <summary>Moving from one slice to another.</summary>
    public static void PlayTick()
    {
        // Sweeping across the wheel quickly must not turn into a buzz.
        var now = Environment.TickCount64;
        if (now - _lastTick < 45)
        {
            return;
        }

        _lastTick = now;
        Play(TickSound);
    }

    /// <summary>The wheel appears.</summary>
    public static void PlayOpen() => Play(OpenSound);

    /// <summary>A conversion finished.</summary>
    public static void PlaySuccess() => Play(SuccessSound);

    /// <summary>A conversion failed.</summary>
    public static void PlayError() => Play(ErrorSound);

    private static void Play(Lazy<IntPtr> sound)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            PlaySound(sound.Value, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Enabled = false;
        }
    }

    // ---- Synthesis: soft sines with a quick attack and an exponential fade ----

    // Rounded and low: slow 12-20 ms attack (no click), gentle decay, quiet, a soft octave overtone for warmth.
    private static float[] Tick() => Tone(0.06, t => 0.045 * Envelope(t, 0.012, 55) * (Math.Sin(2 * Math.PI * 880 * t) + 0.25 * Math.Sin(2 * Math.PI * 1760 * t)));

    private static float[] Open() => Tone(0.16, t =>
    {
        // Soft upward glide, like a bubble.
        var frequency = 440 + 220 * Math.Min(1, t / 0.12);
        return 0.05 * Envelope(t, 0.018, 20) * Math.Sin(2 * Math.PI * frequency * t);
    });

    private static float[] Success() => Tone(0.6, t =>
        0.06 * Note(t, 0.0, 784) + 0.06 * Note(t, 0.1, 1175) + 0.015 * Note(t, 0.1, 2350));

    private static float[] Failure() => Tone(0.5, t => 0.055 * Note(t, 0.0, 392) + 0.055 * Note(t, 0.13, 311));

    private static double Note(double t, double start, double frequency)
    {
        var local = t - start;
        return local < 0 ? 0 : Envelope(local, 0.015, 8) * Math.Sin(2 * Math.PI * frequency * local);
    }

    private static double Envelope(double t, double attack, double decay) =>
        (t < attack ? t / attack : 1) * Math.Exp(-decay * t);

    private static float[] Tone(double seconds, Func<double, double> wave)
    {
        var samples = new float[(int)(seconds * SampleRate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)wave(i / (double)SampleRate);
        }

        // Last 8 ms fade to silence: no click at the end.
        var fade = Math.Min(samples.Length, SampleRate * 8 / 1000);
        for (var i = 0; i < fade; i++)
        {
            samples[samples.Length - 1 - i] *= i / (float)fade;
        }

        return samples;
    }

    /// <summary>16-bit mono PCM WAV in unmanaged memory (never moved by the garbage collector).</summary>
    private static IntPtr Render(Func<float[]> synthesize)
    {
        var samples = synthesize();
        var dataBytes = samples.Length * 2;
        var wav = new byte[44 + dataBytes];
        var span = wav.AsSpan();
        "RIFF"u8.CopyTo(span);
        BitConverter.TryWriteBytes(span[4..], 36 + dataBytes);
        "WAVEfmt "u8.CopyTo(span[8..]);
        BitConverter.TryWriteBytes(span[16..], 16);
        BitConverter.TryWriteBytes(span[20..], (short)1);
        BitConverter.TryWriteBytes(span[22..], (short)1);
        BitConverter.TryWriteBytes(span[24..], SampleRate);
        BitConverter.TryWriteBytes(span[28..], SampleRate * 2);
        BitConverter.TryWriteBytes(span[32..], (short)2);
        BitConverter.TryWriteBytes(span[34..], (short)16);
        "data"u8.CopyTo(span[36..]);
        BitConverter.TryWriteBytes(span[40..], dataBytes);
        for (var i = 0; i < samples.Length; i++)
        {
            BitConverter.TryWriteBytes(span[(44 + i * 2)..], (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue));
        }

        var memory = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, memory, wav.Length);
        return memory;
    }

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
