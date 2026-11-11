using ImageMagick;

namespace Mandarine.Core;

/// <param name="Brightness">-100 to 100.</param>
/// <param name="Contrast">-100 to 100.</param>
/// <param name="Saturation">-100 (grey) to 100 (vivid).</param>
/// <param name="Warmth">-100 (cooler, bluer) to 100 (warmer, redder).</param>
/// <param name="Angle">Degrees, to straighten a photo or a scan.</param>
/// <param name="CropEdges">After turning, cut the empty corners (largest rectangle with the picture's shape).</param>
public sealed record AdjustSettings(double Brightness = 0, double Contrast = 0, double Saturation = 0, double Warmth = 0, double Angle = 0, bool CropEdges = true)
{
    public bool IsNeutral => Brightness == 0 && Contrast == 0 && Saturation == 0 && Warmth == 0 && Angle == 0;
}

/// <summary>Light, colour and angle of a picture. One code path for the live preview and for the saved copy.</summary>
public static class ImageAdjust
{
    /// <summary>The adjusted picture as PNG bytes, optionally shrunk so that its longest side is at most <paramref name="maxSide"/> (for a fast preview).</summary>
    public static byte[] Preview(string path, AdjustSettings settings, int maxSide)
    {
        using var image = Load(path, maxSide);
        return Render(image, settings);
    }

    /// <summary>The picture decoded and shrunk once, to preview many settings without reading the file again.</summary>
    public static MagickImage LoadPreviewBase(string path, int maxSide) => Load(path, maxSide);

    /// <summary>Adjusts a copy of <paramref name="previewBase"/> (it stays untouched). Safe to call from several threads.</summary>
    public static byte[] Preview(MagickImage previewBase, AdjustSettings settings)
    {
        MagickImage copy;
        lock (previewBase)
        {
            copy = (MagickImage)previewBase.Clone();
        }

        using (copy)
        {
            return Render(copy, settings);
        }
    }

    private static byte[] Render(MagickImage image, AdjustSettings settings)
    {
        Apply(image, settings);
        return image.ToByteArray(MagickFormat.Png);
    }

    /// <summary>Writes the adjusted full-size copy, in the format of <paramref name="output"/>.</summary>
    public static void Save(string path, string output, AdjustSettings settings)
    {
        using var image = Load(path, 0);
        Apply(image, settings);
        image.Quality = 95;
        image.Write(output);
    }

    private static MagickImage Load(string path, int maxSide)
    {
        var image = new MagickImage(path);
        image.AutoOrient();
        if (maxSide > 0 && (image.Width > maxSide || image.Height > maxSide))
        {
            image.Resize(new MagickGeometry((uint)maxSide, (uint)maxSide));
        }

        return image;
    }

    private static void Apply(MagickImage image, AdjustSettings s)
    {
        if (s.Brightness != 0 || s.Contrast != 0)
        {
            image.BrightnessContrast(new Percentage(s.Brightness), new Percentage(s.Contrast));
        }

        if (s.Saturation != 0)
        {
            image.Modulate(new Percentage(100), new Percentage(100 + s.Saturation), new Percentage(100));
        }

        if (s.Warmth != 0)
        {
            image.Evaluate(Channels.Red, EvaluateOperator.Multiply, 1 + s.Warmth / 250);
            image.Evaluate(Channels.Blue, EvaluateOperator.Multiply, 1 - s.Warmth / 250);
        }

        if (s.Angle != 0)
        {
            Rotate(image, s.Angle, s.CropEdges);
        }
    }

    private static void Rotate(MagickImage image, double angle, bool cropEdges)
    {
        var width = image.Width;
        var height = image.Height;
        image.BackgroundColor = image.HasAlpha ? MagickColors.Transparent : MagickColors.White;
        image.Rotate(angle);
        image.ResetPage();
        if (!cropEdges)
        {
            return;
        }

        // Largest axis-aligned rectangle with the original shape that fits inside the turned picture.
        var radians = Math.Abs(angle % 180) * Math.PI / 180;
        var longSide = Math.Max((double)width / height, (double)height / width);
        var scale = Math.Clamp(1 / (Math.Cos(radians) + (longSide * Math.Sin(radians))), 0.05, 1);
        var cropWidth = (uint)Math.Max(1, Math.Floor(width * scale));
        var cropHeight = (uint)Math.Max(1, Math.Floor(height * scale));
        image.Crop(new MagickGeometry((int)((image.Width - cropWidth) / 2), (int)((image.Height - cropHeight) / 2), cropWidth, cropHeight));
        image.ResetPage();
    }
}
