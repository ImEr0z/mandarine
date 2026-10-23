using ImageMagick;
using Mandarine.Core.Actions;

namespace Mandarine.Core.Converters;

internal static class ImageFormats
{
    /// <summary>Formats Mandarine can write, keyed by normalized extension.</summary>
    public static readonly IReadOnlyDictionary<string, MagickFormat> Writable = new Dictionary<string, MagickFormat>
    {
        ["png"] = MagickFormat.Png,
        ["jpg"] = MagickFormat.Jpeg,
        ["webp"] = MagickFormat.WebP,
        ["avif"] = MagickFormat.Avif,
        ["gif"] = MagickFormat.Gif,
        ["bmp"] = MagickFormat.Bmp,
        ["tiff"] = MagickFormat.Tiff,
        ["ico"] = MagickFormat.Icon,
        ["jxl"] = MagickFormat.Jxl,
        ["pdf"] = MagickFormat.Pdf,
    };

    private static readonly HashSet<string> Animated = ["gif", "webp"];

    // Every ImageMagick read and write goes through this class: lock the library down before the first one.
    static ImageFormats() => MagickSecurity.EnsureApplied();

    private static readonly HashSet<string> Lossy = ["jpg", "webp", "avif", "jxl", "pdf"];

    public static bool IsImage(string path) => FileKinds.Detect(path) == FileKind.Image;

    public static bool CanAnimate(string extension) => Animated.Contains(Extensions.Normalize(extension));

    public static bool IsLossy(string extension) => Lossy.Contains(Extensions.Normalize(extension));

    /// <summary>
    /// Format used when a tool rewrites an image "in place": the source format when writable,
    /// JPEG for camera formats (HEIC, RAW) and PNG for everything else (PSD, SVG...).
    /// </summary>
    public static string OutputExtensionFor(string input)
    {
        var ext = Extensions.Normalize(FileKinds.ExtensionOf(input));
        if (Writable.ContainsKey(ext))
        {
            return ext;
        }

        return ext is "heic" or "dng" or "cr2" or "cr3" or "nef" or "arw" or "raf" or "orf" or "rw2" or "pef" or "srw"
            ? "jpg"
            : "png";
    }

    public static MagickImage ReadFirstFrame(string path)
    {
        var settings = new MagickReadSettings();
        if (Extensions.SameFormat(FileKinds.ExtensionOf(path), "svg"))
        {
            settings.Density = new Density(192);
            settings.BackgroundColor = MagickColors.Transparent;
        }

        var image = new MagickImage(path, settings);
        image.AutoOrient();
        return image;
    }

    /// <summary>Formats without transparency get a white background instead of black.</summary>
    public static void FlattenIfOpaqueTarget(IMagickImage<byte> image, string targetExtension)
    {
        if (Extensions.Normalize(targetExtension) is "jpg" or "bmp" or "pdf" && image.HasAlpha)
        {
            image.BackgroundColor = MagickColors.White;
            image.Alpha(AlphaAction.Remove);
        }
    }

    public static void Write(MagickImage image, string output, string extension)
    {
        var ext = Extensions.Normalize(extension);
        if (ext == "pdf")
        {
            // One page sized like A4 for the longest side (instead of 1 px = 1/72 in), JPEG-compressed.
            image.Density = new Density(Math.Max(72, Math.Max(image.Width, image.Height) / 11.69));
            image.Settings.Compression = CompressionMethod.JPEG;
            if (image.Quality == 0)
            {
                image.Quality = 85;
            }
        }

        if (ext == "ico")
        {
            if (image.Width > 256 || image.Height > 256)
            {
                image.Resize(new MagickGeometry(256, 256));
            }

            var side = Math.Max(image.Width, image.Height);
            image.BackgroundColor = MagickColors.Transparent;
            image.Extent(side, side, Gravity.Center);
            image.Settings.SetDefine(MagickFormat.Icon, "auto-resize", "256,128,64,48,32,16");
        }

        image.Write(output, Writable[ext]);
    }

    /// <summary>
    /// Applies <paramref name="transform"/> to every frame of an animation (GIF/WebP) or to a single image,
    /// and writes the result. Returns the output path.
    /// </summary>
    public static void Transform(string input, string output, string outputExtension, Action<MagickImage> transform)
    {
        var inputExt = FileKinds.ExtensionOf(input);
        if (CanAnimate(inputExt) && CanAnimate(outputExtension))
        {
            using var frames = new MagickImageCollection(input);
            if (frames.Count > 1)
            {
                frames.Coalesce();
                foreach (var frame in frames)
                {
                    transform((MagickImage)frame);
                }

                frames.Write(output, Writable[Extensions.Normalize(outputExtension)]);
                return;
            }
        }

        using var image = ReadFirstFrame(input);
        transform(image);
        FlattenIfOpaqueTarget(image, outputExtension);
        Write(image, output, outputExtension);
    }
}

/// <summary>Converts any readable image to a target format, preserving animation when both sides support it.</summary>
internal sealed class ImageConvertAction(string extension) : PerFileAction
{
    public override string Id => $"image.to.{extension}";

    public override string Title => extension.ToUpperInvariant();

    public override string Subtitle => "Image";

    public override ActionGroup Group => ActionGroup.Convert;

    public override string TargetExtension => extension;

    public override bool Accepts(string path) => ImageFormats.IsImage(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var output = OutputPaths.ForFile(input, extension, context.OutputDirectory);
            try
            {
                ImageFormats.Transform(input, output, extension, image =>
                {
                    if (ImageFormats.IsLossy(extension))
                    {
                        image.Quality = (uint)Math.Clamp(context.Quality, 1, 100);
                    }
                });
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            return output;
        }, cancellationToken);
}

/// <summary>An image operation that keeps the source format (or the closest writable one).</summary>
internal sealed class ImageToolAction(
    string id,
    string title,
    string glyph,
    string suffix,
    Action<MagickImage, ActionContext> transform) : PerFileAction
{
    public override string Id => id;

    public override string Title => title;

    public override string Glyph => glyph;

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => ImageFormats.IsImage(path);

    protected override Task<string> ProcessAsync(
        string input, ActionContext context, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var extension = ImageFormats.OutputExtensionFor(input);
            var output = OutputPaths.ForFile(input, extension, context.OutputDirectory, suffix);
            try
            {
                ImageFormats.Transform(input, output, extension, image => transform(image, context));
            }
            catch
            {
                OutputPaths.Discard(output);
                throw;
            }

            return output;
        }, cancellationToken);

    public static void RemoveMetadata(IMagickImage<byte> image)
    {
        // Keeps the ICC profile so colors stay identical; orientation was already applied by AutoOrient.
        foreach (var profile in new[] { "exif", "xmp", "iptc", "8bim" })
        {
            image.RemoveProfile(profile);
        }

        foreach (var attribute in image.AttributeNames.ToList())
        {
            if (attribute.StartsWith("comment", StringComparison.OrdinalIgnoreCase))
            {
                image.RemoveAttribute(attribute);
            }
        }
    }

    public static IEnumerable<FileAction> All()
    {
        yield return new ImageToolAction("image.resize.half", "Réduire de 50 %", "\uE71F", "50%",
            (image, _) => image.Resize(new Percentage(50)));
        yield return new ImageToolAction("image.resize.1920", "Max 1920 px", "\uE71F", "1920px",
            (image, _) => image.Resize(new MagickGeometry(1920, 1920) { Greater = true }));
        yield return new ImageToolAction("image.compress", "Compresser", "\uE73F", "compressée",
            (image, context) =>
            {
                RemoveMetadata(image);
                image.Quality = (uint)Math.Min(context.Quality, 72);
                image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
                image.Settings.SetDefine(MagickFormat.WebP, "method", "6");
            });
        // Square, centered crop: Discord / social avatars (512 px is the usual upload size).
        yield return new ImageToolAction("image.icon", "Icône 512 px", "", "icône",
            (image, _) =>
            {
                image.Resize(new MagickGeometry(512, 512) { FillArea = true });
                image.Crop(512, 512, Gravity.Center);
                image.ResetPage();
            });
        yield return new ImageToolAction("image.strip", "Sans métadonnées", "\uEA18", "sans métadonnées",
            (image, _) => RemoveMetadata(image));
        yield return new ImageToolAction("image.rotate.right", "Pivoter à droite", "\uE7A6", "pivotée",
            (image, _) => image.Rotate(90));
        yield return new ImageToolAction("image.rotate.left", "Pivoter à gauche", "\uE7A7", "pivotée",
            (image, _) => image.Rotate(-90));
        yield return new ImageToolAction("image.flop", "Miroir", "\uE8AB", "miroir",
            (image, _) => image.Flop());
        yield return new ImageToolAction("image.grayscale", "Noir et blanc", "\uE790", "noir et blanc",
            (image, _) => image.Grayscale());
        yield return new ImageToolAction("image.trim", "Rogner les marges", "\uE7A8", "rognée",
            (image, _) =>
            {
                image.Trim();
                image.ResetPage();
            });
    }
}

/// <summary>Copies a small image as a data URI, handy for CSS, HTML or Markdown.</summary>
internal sealed class ImageDataUriAction : FileAction
{
    private const long MaxBytes = 8 * 1024 * 1024;

    public override string Id => "image.datauri";

    public override string Title => "Copier en Base64";

    public override string Glyph => "\uE943";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => ImageFormats.IsImage(path);

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override async Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var input = inputs[0];
        if (new FileInfo(input).Length > MaxBytes)
        {
            return new ActionResult([], [new ActionError(input, "Image trop lourde pour le presse-papiers (8 Mo max).")]);
        }

        var bytes = await File.ReadAllBytesAsync(input, cancellationToken).ConfigureAwait(false);
        var mime = Extensions.Normalize(FileKinds.ExtensionOf(input)) switch
        {
            "jpg" => "image/jpeg",
            "svg" => "image/svg+xml",
            "ico" => "image/x-icon",
            "tiff" => "image/tiff",
            var ext => "image/" + ext,
        };
        return ActionResult.Clipboard($"data:{mime};base64,{Convert.ToBase64String(bytes)}");
    }
}
