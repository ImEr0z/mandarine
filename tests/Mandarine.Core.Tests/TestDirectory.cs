using ImageMagick;
using ImageMagick.Drawing;
using Mandarine.Core.Actions;

namespace Mandarine.Core.Tests;

/// <summary>Throw-away working folder plus helpers to create sample inputs and run actions.</summary>
public sealed class TestDirectory : IDisposable
{
    public TestDirectory()
    {
        Path = Directory.CreateTempSubdirectory("mandarine-tests-").FullName;
    }

    public string Path { get; }

    public ActionCatalog Catalog { get; } = new();

    public ActionContext Context { get; } = new() { Quality = 80 };

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public string WriteText(string name, string content)
    {
        var path = File(name);
        System.IO.File.WriteAllText(path, content);
        return path;
    }

    /// <summary>A 320x200 PNG with a transparent border around an orange rectangle.</summary>
    public string CreateImage(string name = "sample.png", uint width = 320, uint height = 200)
    {
        using var image = new MagickImage(MagickColors.Transparent, width, height);
        new Drawables()
            .FillColor(new MagickColor("#FF8A00"))
            .Rectangle(20, 20, width - 20, height - 20)
            .Draw(image);
        var path = File(name);
        image.Write(path);
        return path;
    }

    public string CreateAnimatedGif(string name = "anim.gif", int frames = 4)
    {
        using var collection = new MagickImageCollection();
        for (var i = 0; i < frames; i++)
        {
            var frame = new MagickImage(new MagickColor((byte)(i * 60), 120, 200), 64, 48) { AnimationDelay = 10 };
            collection.Add(frame);
        }

        var path = File(name);
        collection.Write(path);
        return path;
    }

    /// <summary>Hand-written PDF with one line of Helvetica text per page.</summary>
    public string CreateTextPdf(string name, params string[] pages)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            $"<< /Type /Pages /Kids [{string.Join(' ', pages.Select((_, i) => $"{4 + (i * 2)} 0 R"))}] /Count {pages.Length} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        foreach (var text in pages)
        {
            var content = $"BT /F1 18 Tf 20 100 Td ({text}) Tj ET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents {objects.Count + 2} 0 R /Resources << /Font << /F1 3 0 R >> >> >>");
            objects.Add($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");
        }

        var builder = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            builder.Append($"{offset:D10} 00000 n \n");
        }

        builder.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        var path = File(name);
        System.IO.File.WriteAllText(path, builder.ToString(), System.Text.Encoding.ASCII);
        return path;
    }

    public async Task<ActionResult> RunAsync(string actionId, params string[] inputs)
    {
        var action = Catalog.Find(actionId) ?? throw new InvalidOperationException($"Action inconnue : {actionId}");
        Assert.True(action.IsAvailableFor(inputs), $"{actionId} devrait être proposée pour {string.Join(", ", inputs)}");
        var result = await action.RunAsync(inputs, Context, null, CancellationToken.None);
        Assert.Empty(result.Errors);
        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
