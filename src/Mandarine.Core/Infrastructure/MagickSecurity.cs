using ImageMagick;
using ImageMagick.Configuration;

namespace Mandarine.Core;

/// <summary>
/// ImageMagick picks its decoder from the file content, not the extension: a "photo.jpg" can really be
/// MVG or MSL scripts that read other files or fetch URLs. Only plain image coders are allowed, indirect
/// reads ("@file") are refused, and resources are capped so a crafted image cannot exhaust RAM or disk.
/// </summary>
internal static class MagickSecurity
{
    private const string Policy = """
        <policymap>
          <policy domain="path" rights="none" pattern="@*" />
          <policy domain="coder" rights="none" pattern="*" />
          <policy domain="coder" rights="read | write" pattern="{PNG,PNG8,PNG24,PNG32,PNG48,PNG64,PNG00,JPEG,JPG,JPE,JFIF,PJPEG,WEBP,AVIF,HEIC,HEIF,GIF,GIF87,BMP,BMP2,BMP3,DIB,TIFF,TIF,TIFF64,PTIF,ICO,ICON,CUR,TGA,ICB,VDA,VST,PSD,PSB,JXL,JP2,J2C,J2K,JPC,JPX,JPM,PDF,DNG,CR2,CR3,CRW,NEF,NRW,ARW,SR2,SRF,RAF,ORF,RW2,RWL,PEF,SRW,DCRAW,ERF,KDC,MRW,3FR,IIQ,X3F}" />
          <policy domain="coder" rights="read" pattern="{SVG,MSVG,RSVG}" />
        </policymap>
        """;

    // 32 000 px per side covers stitched panoramas; beyond that a header is almost certainly a lie.
    private const uint MaxSide = 32_000;

    private static readonly Lazy<bool> Applied = new(() =>
    {
        var files = ConfigurationFiles.Default;
        files.Policy.Data = Policy;
        MagickNET.Initialize(files);
        ResourceLimits.Width = MaxSide;
        ResourceLimits.Height = MaxSide;
        ResourceLimits.ListLength = 2000;
        ResourceLimits.Disk = 8UL * 1024 * 1024 * 1024;
        ResourceLimits.Time = 600;
        ResourceLimits.LimitMemory(new Percentage(25));
        return true;
    });

    public static void EnsureApplied() => _ = Applied.Value;
}
