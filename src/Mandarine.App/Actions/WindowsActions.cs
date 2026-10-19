using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace Mandarine.App.Actions;

/// <summary>
/// Actions built on what Windows itself provides: text recognition, wallpaper, clipboard, printing.
/// Nothing to install, nothing sent online (Windows OCR runs on the PC).
/// </summary>
internal static class WindowsActions
{
    public static IEnumerable<FileAction> All()
    {
        yield return new OcrAction(toClipboard: false);
        yield return new OcrAction(toClipboard: true);
        yield return new CopyImageAction();
        yield return new WallpaperAction();
        yield return new PrintAction();
    }

    /// <summary>The app's catalog: Mandarine's own conversions plus the Windows features above.</summary>
    public static ActionCatalog Catalog() => new(ActionCatalog.SelfContained().All.Concat(All()));
}

/// <summary>Recognizes the text of an image or a scanned PDF with the OCR engine built into Windows.</summary>
internal sealed class OcrAction(bool toClipboard) : FileAction
{
    public override string Id => toClipboard ? "windows.ocr.copy" : "windows.ocr.txt";

    public override string Title => toClipboard ? "Copier le texte" : "Extraire le texte";

    public override string Glyph => toClipboard ? "" : "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Raster.IsImage(path) || Raster.IsPdf(path);

    public override async Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages()
            ?? throw new InvalidOperationException("Aucune langue de reconnaissance de texte n'est installée dans Windows "
                + "(Paramètres > Heure et langue > Langue > Options de la langue).");

        var outputs = new List<string>();
        var errors = new List<ActionError>();
        var copied = new StringBuilder();
        for (var i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ActionProgress(i, inputs.Count, inputs[i], null));
            try
            {
                var text = new StringBuilder();
                var pages = Raster.IsPdf(inputs[i])
                    ? Raster.PdfPagesToPng(inputs[i], 200)
                    : [Raster.ImageToPng(inputs[i], OcrEngine.MaxImageDimension)];
                foreach (var png in pages)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    text.AppendLine(await RecognizeAsync(engine, png).ConfigureAwait(false));
                }

                var result = text.ToString().Trim();
                if (result.Length == 0)
                {
                    throw new InvalidOperationException("Aucun texte reconnu.");
                }

                if (toClipboard)
                {
                    copied.AppendLine(result);
                }
                else
                {
                    var output = OutputPaths.ForFile(inputs[i], "txt", context.OutputDirectory, "texte");
                    await File.WriteAllTextAsync(output, result + Environment.NewLine, new UTF8Encoding(false), cancellationToken)
                        .ConfigureAwait(false);
                    outputs.Add(output);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add(new ActionError(inputs[i], ex.Message));
            }
        }

        return toClipboard && copied.Length > 0
            ? new ActionResult([], errors, copied.ToString().TrimEnd())
            : new ActionResult(outputs, errors);
    }

    private static async Task<string> RecognizeAsync(OcrEngine engine, byte[] png)
    {
        using var stream = new MemoryStream(png).AsRandomAccessStream();
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap);
        return string.Join(Environment.NewLine, result.Lines.Select(l => l.Text));
    }
}

/// <summary>Puts the picture itself on the clipboard, ready to paste in a mail, a chat or a document.</summary>
internal sealed class CopyImageAction : FileAction
{
    public override string Id => "windows.copy.image";

    public override string Title => "Copier l'image";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsImage(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        var png = Raster.ImageToPng(inputs[0], 8000);
        StaThread.Run(() =>
        {
            var bitmap = new PngBitmapDecoder(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            var data = new DataObject();
            data.SetImage(bitmap);
            data.SetData("PNG", new MemoryStream(png)); // keeps transparency for apps that read PNG
            // copy: true renders the data now, so it stays on the clipboard after this process exits.
            Clipboard.SetDataObject(data, copy: true);
        });
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Sets the image as desktop background, as "Définir comme arrière-plan" does in Explorer.</summary>
internal sealed class WallpaperAction : FileAction
{
    private const int SPI_SETDESKWALLPAPER = 0x0014;
    private const int SPIF_UPDATEINIFILE = 0x01;
    private const int SPIF_SENDCHANGE = 0x02;

    public override string Id => "windows.wallpaper";

    public override string Title => "Fond d'écran";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsImage(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        // Windows reads JPEG, PNG and BMP; anything else (HEIC, WebP, RAW…) is converted into a private copy.
        var source = inputs[0];
        var ext = Path.GetExtension(source).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".bmp"))
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarine", "Fonds d'écran");
            Directory.CreateDirectory(folder);
            var copy = Path.Combine(folder, Path.GetFileNameWithoutExtension(source) + ".png");
            File.WriteAllBytes(copy, Raster.ImageToPng(source, 8000));
            source = copy;
        }

        if (!SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, Path.GetFullPath(source), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE))
        {
            throw new InvalidOperationException("Windows a refusé de changer le fond d'écran.");
        }

        return Task.FromResult(new ActionResult([], []));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(int action, int param, string value, int winIni);
}

/// <summary>Opens the Windows print dialog for images and PDF (the file's usual printing app).</summary>
internal sealed class PrintAction : FileAction
{
    public override string Id => "windows.print";

    public override string Title => "Imprimer";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool Accepts(string path) => Raster.IsImage(path) || Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        var errors = new List<ActionError>();
        foreach (var input in inputs)
        {
            try
            {
                Process.Start(new ProcessStartInfo(input) { UseShellExecute = true, Verb = "print" })?.Dispose();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                errors.Add(new ActionError(input, $"Aucune application ne sait imprimer ce fichier ({ex.Message})."));
            }
        }

        return Task.FromResult(new ActionResult([], errors));
    }
}

internal static class StaThread
{
    /// <summary>Clipboard and WPF imaging need a single-threaded apartment; worker threads are not.</summary>
    public static void Run(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
