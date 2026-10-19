using System.Globalization;
using Mandarine.Core.Actions;

namespace Mandarine.Core.Converters;

internal static class Ffmpeg
{
    private const string EvenDimensions = "scale=trunc(iw/2)*2:trunc(ih/2)*2";

    public static readonly IReadOnlyDictionary<string, string[]> AudioCodecs = new Dictionary<string, string[]>
    {
        ["mp3"] = ["-c:a", "libmp3lame", "-q:a", "2"],
        ["m4a"] = ["-c:a", "aac", "-b:a", "256k"],
        ["wav"] = ["-c:a", "pcm_s16le"],
        ["flac"] = ["-c:a", "flac"],
        ["ogg"] = ["-c:a", "libvorbis", "-q:a", "6"],
        ["opus"] = ["-c:a", "libopus", "-b:a", "160k"],
        ["aiff"] = ["-c:a", "pcm_s16be"],
    };

    public static readonly IReadOnlyDictionary<string, string[]> VideoCodecs = new Dictionary<string, string[]>
    {
        ["mp4"] = [.. H264(20, "medium", "192k"), "-movflags", "+faststart"],
        ["mov"] = [.. H264(20, "medium", "192k")],
        ["mkv"] = [.. H264(20, "medium", "192k")],
        ["webm"] = ["-c:v", "libvpx-vp9", "-crf", "32", "-b:v", "0", "-row-mt", "1", "-c:a", "libopus", "-b:a", "128k"],
        ["avi"] = ["-c:v", "mpeg4", "-vtag", "xvid", "-q:v", "3", "-c:a", "libmp3lame", "-q:a", "3"],
        ["gif"] =
        [
            "-vf",
            "fps=12,scale='min(720,iw)':-1:flags=lanczos,split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4",
            "-loop", "0",
        ],
    };

    public static string[] H264(int crf, string preset, string audioBitrate) =>
    [
        "-vf", EvenDimensions, "-c:v", "libx264", "-preset", preset, "-crf", crf.ToString(CultureInfo.InvariantCulture),
        "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", audioBitrate,
    ];

    /// <summary>Runs ffmpeg on one input, reporting progress from its "-progress" output.</summary>
    public static async Task RunAsync(
        ActionContext context,
        string input,
        IEnumerable<string> outputArguments,
        string output,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var ffmpeg = context.Tools.Require(ExternalTool.Ffmpeg);
        string[] arguments =
        [
            "-hide_banner", "-nostdin", "-y", "-nostats", "-progress", "pipe:1",
            "-i", input,
            "-sn", "-map_metadata", "0",
            .. outputArguments,
            output,
        ];

        TimeSpan? duration = null;
        try
        {
            await ProcessRunner.RunAsync(ffmpeg, arguments, cancellationToken,
                onOutput: line =>
                {
                    // "out_time_us=12345678" lines arrive a few times per second.
                    if (duration is { TotalMicroseconds: > 0 } total
                        && line.StartsWith("out_time_us=", StringComparison.Ordinal)
                        && long.TryParse(line.AsSpan(12), out var microseconds))
                    {
                        progress?.Report(Math.Clamp(microseconds / total.TotalMicroseconds, 0, 1));
                    }
                },
                onError: line =>
                {
                    // Header line: "  Duration: 00:01:23.45, start: ..."
                    var trimmed = line.TrimStart();
                    if (duration is null && trimmed.StartsWith("Duration:", StringComparison.Ordinal)
                        && TimeSpan.TryParse(trimmed.AsSpan(9, Math.Min(12, trimmed.Length - 9)).Trim().TrimEnd(','),
                            CultureInfo.InvariantCulture, out var parsed))
                    {
                        duration = parsed;
                    }
                }).ConfigureAwait(false);
        }
        catch
        {
            OutputPaths.Discard(output);
            throw;
        }
    }
}

/// <summary>A single ffmpeg invocation per input file.</summary>
internal sealed class FfmpegAction(
    string id,
    string title,
    ActionGroup group,
    Func<string, bool> accepts,
    Func<string, string> outputExtension,
    Func<string, IEnumerable<string>> arguments,
    string? subtitle = null,
    string glyph = "\uE714",
    string? suffix = null,
    string? targetExtension = null) : PerFileAction
{
    public override string Id => id;

    public override string Title => title;

    public override string? Subtitle => subtitle;

    public override string Glyph => glyph;

    public override ActionGroup Group => group;

    public override ExternalTool Requires => ExternalTool.Ffmpeg;

    public override string? TargetExtension => targetExtension;

    public override bool Accepts(string path) => accepts(path);

    protected override async Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var output = OutputPaths.ForFile(input, outputExtension(input), context.OutputDirectory, suffix);
        await Ffmpeg.RunAsync(context, input, arguments(input), output, progress, cancellationToken).ConfigureAwait(false);
        return output;
    }
}

internal static class MediaActions
{
    private static bool IsAudio(string path) => FileKinds.Detect(path) == FileKind.Audio;

    private static bool IsVideo(string path) => FileKinds.Detect(path) == FileKind.Video;

    private static bool IsGif(string path) => Extensions.SameFormat(FileKinds.ExtensionOf(path), "gif");

    private static readonly string[] AudioTargets = ["mp3", "m4a", "wav", "flac", "ogg", "opus", "aiff"];

    private static readonly string[] VideoTargets = ["mp4", "mov", "mkv", "webm", "avi", "gif"];

    private static readonly string[] AudioFromVideo = ["mp3", "m4a", "wav"];

    public static IEnumerable<FileAction> All()
    {
        foreach (var ext in AudioTargets)
        {
            yield return new FfmpegAction($"audio.to.{ext}", ext.ToUpperInvariant(), ActionGroup.Convert,
                IsAudio, _ => ext, _ => ["-vn", .. Ffmpeg.AudioCodecs[ext]],
                subtitle: "Audio", targetExtension: ext);
        }

        foreach (var ext in VideoTargets)
        {
            // Animated GIFs can become real videos (much lighter), the other way round is "GIF" itself.
            Func<string, bool> accepts = ext is "mp4" or "webm" ? p => IsVideo(p) || IsGif(p) : IsVideo;
            yield return new FfmpegAction($"video.to.{ext}", ext.ToUpperInvariant(), ActionGroup.Convert,
                accepts, _ => ext, _ => Ffmpeg.VideoCodecs[ext],
                subtitle: ext == "gif" ? "Animation" : "Vidéo", targetExtension: ext);
        }

        foreach (var ext in AudioFromVideo)
        {
            yield return new FfmpegAction($"video.audio.{ext}", ext.ToUpperInvariant(), ActionGroup.Convert,
                IsVideo, _ => ext, _ => ["-vn", .. Ffmpeg.AudioCodecs[ext]],
                subtitle: "Piste audio");
        }

        yield return new FfmpegAction("video.compress", "Compresser", ActionGroup.Tools,
            IsVideo, _ => "mp4", _ => [.. Ffmpeg.H264(28, "slow", "128k"), "-movflags", "+faststart"],
            glyph: "\uE73F", suffix: "compressée");
        yield return new FfmpegAction("video.720p", "Réduire en 720p", ActionGroup.Tools,
            IsVideo, _ => "mp4",
            _ =>
            [
                "-vf", "scale=-2:'min(720,ih)'", "-c:v", "libx264", "-preset", "medium", "-crf", "22",
                "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "160k", "-movflags", "+faststart",
            ],
            glyph: "\uE7F4", suffix: "720p");
        yield return new FfmpegAction("video.mute", "Supprimer le son", ActionGroup.Tools,
            IsVideo, FileKinds.ExtensionOf, _ => ["-c:v", "copy", "-an"],
            glyph: "\uE74F", suffix: "sans son");
        yield return new FfmpegAction("video.poster", "Première image", ActionGroup.Tools,
            IsVideo, _ => "png", _ => ["-frames:v", "1", "-update", "1"],
            glyph: "\uE91B");
        yield return new FfmpegAction("audio.normalize", "Normaliser le volume", ActionGroup.Tools,
            IsAudio, NormalizedExtension,
            input => ["-vn", "-af", "loudnorm=I=-16:TP=-1.5:LRA=11", .. Ffmpeg.AudioCodecs[NormalizedExtension(input)]],
            glyph: "\uE767", suffix: "normalisé");
    }

    private static string NormalizedExtension(string input)
    {
        var ext = Extensions.Normalize(FileKinds.ExtensionOf(input));
        return Ffmpeg.AudioCodecs.ContainsKey(ext) ? ext : "m4a";
    }
}
