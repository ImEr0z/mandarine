using ImageMagick;

namespace Mandarine.Core.Tests;

public class ImageTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    [Theory]
    [InlineData("jpg", MagickFormat.Jpeg)]
    [InlineData("webp", MagickFormat.WebP)]
    [InlineData("avif", MagickFormat.Avif)]
    [InlineData("gif", MagickFormat.Gif)]
    [InlineData("bmp", MagickFormat.Bmp)]
    [InlineData("tiff", MagickFormat.Tiff)]
    [InlineData("ico", MagickFormat.Ico)]
    [InlineData("jxl", MagickFormat.Jxl)]
    public async Task Png_converts_to(string extension, MagickFormat expected)
    {
        var result = await _dir.RunAsync($"image.to.{extension}", _dir.CreateImage());

        var output = Assert.Single(result.OutputFiles);
        Assert.Equal(_dir.File($"sample.{extension}"), output);
        var info = new MagickImageInfo(output);
        Assert.Equal(expected, info.Format);
    }

    [Fact]
    public async Task Jpeg_output_has_white_background_instead_of_black()
    {
        var result = await _dir.RunAsync("image.to.jpg", _dir.CreateImage());

        using var image = new MagickImage(result.OutputFiles[0]);
        var corner = image.GetPixels().GetPixel(2, 2).ToColor()!;
        Assert.True(corner.R > 240 && corner.G > 240 && corner.B > 240, $"Coin {corner}");
    }

    [Fact]
    public async Task Ico_is_square_and_at_most_256px()
    {
        var result = await _dir.RunAsync("image.to.ico", _dir.CreateImage(width: 600, height: 300));

        using var icon = new MagickImageCollection(result.OutputFiles[0]);
        Assert.Equal(256u, icon.Max(f => f.Width));
        Assert.All(icon, frame => Assert.Equal(frame.Width, frame.Height));
    }

    [Fact]
    public async Task Svg_input_is_rasterized()
    {
        var svg = _dir.WriteText("logo.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20"><rect width="40" height="20" fill="#ff8a00"/></svg>""");
        var png = (await _dir.RunAsync("image.to.png", svg)).OutputFiles[0];
        var info = new MagickImageInfo(png);
        Assert.Equal(MagickFormat.Png, info.Format);
        Assert.Equal(80u, info.Width);
    }

    [Theory]
    [InlineData(MagickFormat.Heic)]
    [InlineData(MagickFormat.Avif)]
    [InlineData(MagickFormat.Dng)]
    [InlineData(MagickFormat.Psd)]
    [InlineData(MagickFormat.Jxl)]
    public void Camera_and_modern_formats_can_be_read(MagickFormat format)
    {
        // HEIC encoding is not shipped with Magick.NET (patents), decoding is.
        Assert.True(MagickFormatInfo.Create(format)?.SupportsReading, $"{format} illisible");
    }

    [Fact]
    public async Task Animated_gif_stays_animated_as_webp()
    {
        var result = await _dir.RunAsync("image.to.webp", _dir.CreateAnimatedGif(frames: 4));

        using var frames = new MagickImageCollection(result.OutputFiles[0]);
        Assert.Equal(4, frames.Count);
    }

    [Fact]
    public async Task Resize_and_rotate_tools()
    {
        var input = _dir.CreateImage();

        var half = (await _dir.RunAsync("image.resize.half", input)).OutputFiles[0];
        Assert.Equal(_dir.File("sample - 50%.png"), half);
        Assert.Equal(160u, new MagickImageInfo(half).Width);

        var rotated = (await _dir.RunAsync("image.rotate.right", input)).OutputFiles[0];
        var info = new MagickImageInfo(rotated);
        Assert.Equal((200u, 320u), (info.Width, info.Height));

        var big = _dir.CreateImage("big.png", 3000, 1000);
        var bounded = (await _dir.RunAsync("image.resize.1920", big)).OutputFiles[0];
        Assert.Equal(1920u, new MagickImageInfo(bounded).Width);

        var small = (await _dir.RunAsync("image.resize.1920", input)).OutputFiles[0];
        Assert.Equal(320u, new MagickImageInfo(small).Width);
    }

    [Fact]
    public async Task Trim_removes_transparent_margins()
    {
        var trimmed = (await _dir.RunAsync("image.trim", _dir.CreateImage())).OutputFiles[0];

        var info = new MagickImageInfo(trimmed);
        Assert.Equal((281u, 161u), (info.Width, info.Height));
    }

    [Fact]
    public async Task Strip_removes_exif_but_keeps_pixels()
    {
        var input = _dir.File("exif.jpg");
        using (var image = new MagickImage(_dir.CreateImage()))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Make, "Mandarine Camera");
            exif.SetValue(ExifTag.GPSLatitudeRef, "N");
            image.SetProfile(exif);
            image.Write(input, MagickFormat.Jpeg);
        }

        var stripped = (await _dir.RunAsync("image.strip", input)).OutputFiles[0];

        using var result = new MagickImage(stripped);
        Assert.Null(result.GetExifProfile());
        Assert.Equal(320u, result.Width);
    }

    [Fact]
    public void Tools_keep_the_format_or_fall_back_to_a_writable_one()
    {
        Assert.Equal("png", Converters.ImageFormats.OutputExtensionFor("a.png"));
        Assert.Equal("jpg", Converters.ImageFormats.OutputExtensionFor("a.JPEG"));
        Assert.Equal("jpg", Converters.ImageFormats.OutputExtensionFor("IMG_0001.HEIC"));
        Assert.Equal("jpg", Converters.ImageFormats.OutputExtensionFor("DSC_0001.NEF"));
        Assert.Equal("png", Converters.ImageFormats.OutputExtensionFor("maquette.psd"));
        Assert.Equal("png", Converters.ImageFormats.OutputExtensionFor("logo.svg"));
    }

    [Fact]
    public async Task Images_combine_into_one_pdf_page_each()
    {
        var result = await _dir.RunAsync("image.combine.pdf", _dir.CreateImage("a.png"), _dir.CreateImage("b.png"));

        var pdf = Assert.Single(result.OutputFiles);
        Assert.Equal(2, PDFtoImage.Conversion.GetPageCount(File.ReadAllBytes(pdf)));
    }

    [Fact]
    public async Task Data_uri_goes_to_clipboard()
    {
        var result = await _dir.RunAsync("image.datauri", _dir.CreateImage());

        Assert.Empty(result.OutputFiles);
        Assert.StartsWith("data:image/png;base64,iVBOR", result.ClipboardText);
    }

    public void Dispose() => _dir.Dispose();
}
