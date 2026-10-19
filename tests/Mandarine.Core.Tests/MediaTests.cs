using Mandarine.Core.Actions;

namespace Mandarine.Core.Tests;

/// <summary>Runs only where FFmpeg is installed (skipped otherwise).</summary>
public class MediaTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private string Ffmpeg
    {
        get
        {
            var path = _dir.Context.Tools.Find(ExternalTool.Ffmpeg);
            Skip.If(path is null, "FFmpeg absent");
            return path!;
        }
    }

    private async Task<string> GenerateAsync(string name, params string[] arguments)
    {
        var output = _dir.File(name);
        await ProcessRunner.RunAsync(Ffmpeg, ["-hide_banner", "-loglevel", "error", "-y", .. arguments, output], CancellationToken.None);
        return output;
    }

    private Task<string> ToneAsync() =>
        GenerateAsync("tone.wav", "-f", "lavfi", "-i", "sine=frequency=440:duration=2");

    private Task<string> ClipAsync() =>
        GenerateAsync("clip.mov", "-f", "lavfi", "-i", "testsrc=size=321x241:rate=25:duration=2",
            "-f", "lavfi", "-i", "sine=duration=2", "-shortest", "-c:v", "mpeg4", "-c:a", "aac");

    [SkippableTheory]
    [InlineData("mp3")]
    [InlineData("m4a")]
    [InlineData("flac")]
    [InlineData("ogg")]
    [InlineData("opus")]
    [InlineData("aiff")]
    public async Task Audio_converts_to(string extension)
    {
        var output = (await _dir.RunAsync($"audio.to.{extension}", await ToneAsync())).OutputFiles[0];

        Assert.Equal(_dir.File($"tone.{extension}"), output);
        Assert.True(new FileInfo(output).Length > 1000);
    }

    [SkippableTheory]
    [InlineData("video.to.mp4", "clip.mp4")]
    [InlineData("video.to.webm", "clip.webm")]
    [InlineData("video.to.gif", "clip.gif")]
    [InlineData("video.audio.mp3", "clip.mp3")]
    [InlineData("video.compress", "clip - compressée.mp4")]
    [InlineData("video.mute", "clip - sans son.mov")]
    [InlineData("video.poster", "clip.png")]
    public async Task Video_actions(string actionId, string expectedName)
    {
        var output = (await _dir.RunAsync(actionId, await ClipAsync())).OutputFiles[0];

        Assert.Equal(_dir.File(expectedName), output);
        Assert.True(new FileInfo(output).Length > 500);
    }

    [SkippableFact]
    public async Task Animated_gif_becomes_mp4()
    {
        var output = (await _dir.RunAsync("video.to.mp4", _dir.CreateAnimatedGif())).OutputFiles[0];

        Assert.EndsWith("anim.mp4", output);
    }

    [SkippableFact]
    public async Task Long_conversions_report_progress_within_the_file()
    {
        var input = await ClipAsync();
        var fractions = new List<double>();
        var action = _dir.Catalog.Find("video.to.mp4")!;

        await action.RunAsync([input], _dir.Context,
            new SyncProgress(p => { if (p.FileFraction is { } f) fractions.Add(f); }), CancellationToken.None);

        Assert.NotEmpty(fractions);
        Assert.True(fractions.Max() > 0.9, string.Join(", ", fractions));
    }

    [SkippableFact]
    public async Task Broken_input_is_reported_and_leaves_no_output()
    {
        _ = Ffmpeg;
        var broken = _dir.WriteText("broken.mp4", "pas une vidéo");
        var result = await _dir.Catalog.Find("video.to.webm")!.RunAsync([broken], _dir.Context, null, CancellationToken.None);

        var error = Assert.Single(result.Errors);
        Assert.Contains("ffmpeg a échoué", error.Message);
        Assert.False(File.Exists(_dir.File("broken.webm")));
    }

    [Fact]
    public async Task Missing_ffmpeg_raises_a_dedicated_exception()
    {
        var context = new ActionContext
        {
            Tools = new ToolLocator(new Dictionary<ExternalTool, string?> { [ExternalTool.Ffmpeg] = "/nonexistent/ffmpeg" }),
        };
        var action = _dir.Catalog.Find("audio.to.mp3")!;
        var input = _dir.WriteText("a.wav", "x");

        if (context.Tools.Find(ExternalTool.Ffmpeg) is null)
        {
            var ex = await Assert.ThrowsAsync<MissingToolException>(() =>
                action.RunAsync([input], context, null, CancellationToken.None));
            Assert.Equal(ExternalTool.Ffmpeg, ex.Tool);
        }
    }

    public void Dispose() => _dir.Dispose();

    private sealed class SyncProgress(Action<ActionProgress> handler) : IProgress<ActionProgress>
    {
        public void Report(ActionProgress value) => handler(value);
    }
}
