using System.Buffers.Binary;
using ImageMagick;

namespace Mandarine.Core.Tests;

/// <summary>Hostile inputs: files whose content does not match their name, or that try to escape the conversion.</summary>
public class SecurityTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    [Fact]
    public async Task Image_script_disguised_as_jpeg_is_refused()
    {
        var secret = _dir.WriteText("secret.txt", "mot de passe");
        // MVG is ImageMagick's drawing language: it can pull other files (or URLs) into the picture.
        var evil = _dir.WriteText("vacances.jpg",
            $"push graphic-context\nviewbox 0 0 64 64\nimage over 0,0 64,64 'text:{secret.Replace('\\', '/')}'\npop graphic-context\n");

        var result = await _dir.Catalog.Find("image.to.png")!.RunAsync([evil], _dir.Context, null, CancellationToken.None);

        Assert.Single(result.Errors);
        Assert.False(File.Exists(_dir.File("vacances.png")));
    }

    [Fact]
    public async Task Msl_script_disguised_as_png_is_refused()
    {
        var evil = _dir.WriteText("icone.png",
            """<?xml version="1.0" encoding="UTF-8"?><image><read filename="text:C:/Windows/win.ini"/><write filename="vol.png"/></image>""");

        var result = await _dir.Catalog.Find("image.to.jpg")!.RunAsync([evil], _dir.Context, null, CancellationToken.None);

        Assert.Single(result.Errors);
        Assert.False(File.Exists(_dir.File("vol.png")));
    }

    [Fact]
    public async Task Gigantic_image_header_fails_fast_instead_of_exhausting_memory()
    {
        var bomb = _dir.File("bombe.png");
        await File.WriteAllBytesAsync(bomb, PngWithClaimedSize(100_000, 100_000));

        var result = await _dir.Catalog.Find("image.to.jpg")!.RunAsync([bomb], _dir.Context, null, CancellationToken.None);

        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task Svg_still_renders_with_the_locked_down_policy()
    {
        var svg = _dir.WriteText("logo.svg",
            """<svg xmlns="http://www.w3.org/2000/svg" width="40" height="20"><rect width="40" height="20" fill="#ff8a00"/></svg>""");

        var png = (await _dir.RunAsync("image.to.png", svg)).OutputFiles[0];

        using var image = new MagickImage(png);
        Assert.True(image.Width > 0);
    }

    /// <summary>A 1x1 PNG whose header then claims another size (CRC fixed so the decoder trusts it).</summary>
    private static byte[] PngWithClaimedSize(uint width, uint height)
    {
        using var image = new MagickImage(MagickColors.Orange, 1, 1);
        var bytes = image.ToByteArray(MagickFormat.Png);
        // Signature (8) + IHDR length (4) + "IHDR" (4): width and height follow.
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29), Crc32(bytes.AsSpan(12, 17)));
        return bytes;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }

    public void Dispose() => _dir.Dispose();
}
