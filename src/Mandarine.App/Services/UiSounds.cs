using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Mandarine.App.Services;

/// <summary>Sound family for the hover and end-of-conversion sounds.</summary>
public enum SoundTheme
{
    Soft,
    Glass,
    Drop,
    Silk,
    Off,
}

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

    private static readonly ConcurrentDictionary<(string, SoundTheme), IntPtr> Cache = new();
    private static long _lastTick;

    public static bool Enabled { get; set; } = true;

    /// <summary>Theme of the sounds played while hovering the wheel.</summary>
    public static SoundTheme Hover { get; set; } = SoundTheme.Soft;

    /// <summary>Theme of the sounds played when a conversion ends.</summary>
    public static SoundTheme Done { get; set; } = SoundTheme.Soft;

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
        Play("tick", Hover);
    }

    /// <summary>The wheel appears.</summary>
    public static void PlayOpen() => Play("open", Hover);

    /// <summary>A conversion finished.</summary>
    public static void PlaySuccess() => Play("success", Done);

    /// <summary>A conversion failed.</summary>
    public static void PlayError() => Play("error", Done);

    /// <summary>Plays a theme from the settings window, whatever the master switch says.</summary>
    public static void Preview(SoundTheme theme, bool hover) => Play(hover ? "open" : "success", theme, force: true);

    private static void Play(string name, SoundTheme theme, bool force = false)
    {
        if ((!Enabled && !force) || theme == SoundTheme.Off)
        {
            return;
        }

        try
        {
            var sound = Cache.GetOrAdd((name, theme), key => Render(() => Synthesize(key.Item1, key.Item2)));
            PlaySound(sound, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Enabled = false;
        }
    }

    // ---- Synthesis: soft sines with a quick attack and an exponential fade ----

    private static float[] Synthesize(string name, SoundTheme theme) => (name, theme) switch
    {
        // Soft: rounded and low, slow attack (no click), gentle decay, a soft octave overtone for warmth.
        ("tick", SoundTheme.Soft) => Tone(0.06, t => 0.045 * Envelope(t, 0.012, 55) * (Sin(880, t) + 0.25 * Sin(1760, t))),
        ("tick", SoundTheme.Glass) => Tone(0.12, t => 0.04 * Envelope(t, 0.006, 30) * (Sin(1320, t) + 0.35 * Sin(3643, t))),
        ("tick", SoundTheme.Drop) => Tone(0.08, t => 0.05 * Envelope(t, 0.008, 55) * Sin(500 + 700 * Math.Min(1, t / 0.04), t)),
        ("tick", SoundTheme.Silk) => Tone(0.12, t => 0.04 * Envelope(t, 0.02, 30) * (Sin(330, t) + 0.5 * Sin(495, t))),

        ("open", SoundTheme.Soft) => Tone(0.16, t => 0.05 * Envelope(t, 0.018, 20) * Sin(440 + 220 * Math.Min(1, t / 0.12), t)),
        ("open", SoundTheme.Glass) => Tone(0.4, t => 0.045 * Note(t, 0.0, 1320, 12) + 0.045 * Note(t, 0.07, 1760, 12)),
        ("open", SoundTheme.Drop) => Tone(0.18, t => 0.055 * Envelope(t, 0.01, 22) * Sin(400 + 500 * Math.Min(1, t / 0.08), t)),
        ("open", SoundTheme.Silk) => Tone(0.45, t => 0.03 * Pad(t, 0, 220, 0.05, 9) + 0.025 * Pad(t, 0, 330, 0.05, 9) + 0.02 * Pad(t, 0, 440, 0.05, 9)),

        ("success", SoundTheme.Soft) => Tone(0.6, t => 0.06 * Note(t, 0.0, 784) + 0.06 * Note(t, 0.1, 1175) + 0.015 * Note(t, 0.1, 2350)),
        ("success", SoundTheme.Glass) => Tone(0.9, t => 0.05 * Note(t, 0.0, 1047, 5) + 0.05 * Note(t, 0.12, 1568, 5) + 0.015 * Note(t, 0.12, 4326, 7)),
        ("success", SoundTheme.Drop) => Tone(0.45, t => 0.055 * Envelope(t, 0.01, 12) * Sin(600 + 400 * Math.Min(1, t / 0.07), t) + 0.045 * Note(t, 0.14, 1200, 14)),
        ("success", SoundTheme.Silk) => Tone(1.2, t => 0.035 * (Pad(t, 0, 523, 0.06, 4) + Pad(t, 0.04, 659, 0.06, 4) + Pad(t, 0.08, 784, 0.06, 4))),

        ("error", SoundTheme.Soft) => Tone(0.5, t => 0.055 * Note(t, 0.0, 392) + 0.055 * Note(t, 0.13, 311)),
        ("error", SoundTheme.Glass) => Tone(0.7, t => 0.05 * Note(t, 0.0, 698, 7) + 0.05 * Note(t, 0.13, 523, 7)),
        ("error", SoundTheme.Drop) => Tone(0.3, t => 0.055 * Envelope(t, 0.01, 12) * Sin(600 - 300 * Math.Min(1, t / 0.12), t)),
        ("error", SoundTheme.Silk) => Tone(0.9, t => 0.035 * (Pad(t, 0, 262, 0.05, 5) + Pad(t, 0, 311, 0.05, 5))),
        _ => [0f],
    };

    private static double Sin(double frequency, double t) => Math.Sin(2 * Math.PI * frequency * t);

    private static double Pad(double t, double start, double frequency, double attack, double decay)
    {
        var local = t - start;
        return local < 0 ? 0 : Envelope(local, attack, decay) * Sin(frequency, local);
    }

    private static double Note(double t, double start, double frequency, double decay = 8) =>
        Pad(t, start, frequency, 0.015, decay);

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
