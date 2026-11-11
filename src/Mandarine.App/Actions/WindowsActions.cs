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
    private static bool PdfOnly(string path) => Raster.IsPdf(path);

    private static bool FolderOnly(string path) => Directory.Exists(path);

    private static bool ImageOnly(string path) =>
        Array.IndexOf(Views.ImageCropWindow.Extensions, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) >= 0;

    public static IEnumerable<FileAction> All()
    {
        yield return new OcrAction(toClipboard: false);
        yield return new OcrAction(toClipboard: true);
        yield return new CopyImageAction();
        yield return new WallpaperAction();
        yield return new PdfEditAction();
        yield return new PdfOrganizeAction();
        yield return new PdfCompareAction();
        yield return new PdfSummaryAction();
        yield return new ImageCropAction();
        yield return new PdfRedactAction();
        yield return new PdfPreviewAction("pdf.watermark", "Filigrane", "\uE7B8");
        yield return new PdfPreviewAction("pdf.crop", "Recadrer", "\uE7A8");
        yield return new BulkRenameAction();
        yield return new WindowAction("pdf.metadata", "Métadonnées", "\uE946", Services.CommandLine.MetadataPdfArgument, PdfOnly);
        yield return new WindowAction("folder.sort", "Trier le dossier", "\uE8CB", Services.CommandLine.SortFolderArgument, FolderOnly);
        yield return new WindowAction("folder.duplicates", "Trouver les doublons", "\uE8C8", Services.CommandLine.FindDuplicatesArgument, FolderOnly);
        yield return new WindowAction("image.adjust", "Ajuster / redresser", "\uE706", Services.CommandLine.AdjustImageArgument, ImageOnly);
        yield return new ImageMarkupAction(blur: false);
        yield return new ImageMarkupAction(blur: true);
        yield return new PrintAction();
    }

    /// <summary>The app's catalog: Mandarine's own conversions plus the Windows features above.</summary>
    public static ActionCatalog Catalog() =>
        new(ActionCatalog.SelfContained().All.Where(a => a.Id is not ("pdf.watermark" or "pdf.crop")).Concat(All()));
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

/// <summary>Opens the PDF text editor (a separate Mandarine.exe process, so that pdfium never runs in the tray app).</summary>
internal sealed class PdfEditAction : FileAction
{
    public override string Id => "pdf.edit";

    public override string Title => "Modifier le texte";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.EditPdfArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>PDF windows run in their own process: pdfium stays out of the tray app.</summary>
internal static class PdfWindowLauncher
{
    public static void Start(string argument, IReadOnlyList<string> files)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Mandarine.exe"))
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        start.ArgumentList.Add(argument);
        foreach (var file in files)
        {
            start.ArgumentList.Add(file);
        }

        Process.Start(start)?.Dispose();
    }
}

/// <summary>Reorder, duplicate or delete the pages of a PDF on a wall of thumbnails.</summary>
internal sealed class PdfOrganizeAction : FileAction
{
    public override string Id => "pdf.organize";

    public override string Title => "Réorganiser les pages";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.OrganizePdfArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Shows what changed between two versions of a PDF (select both, the older one first by name).</summary>
internal sealed class PdfCompareAction : FileAction
{
    public override string Id => "pdf.compare";

    public override string Title => "Comparer les deux PDF";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 2 && inputs.All(Accepts);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        // Older version first: by last write time, the earlier file is the "before".
        var ordered = inputs.OrderBy(File.GetLastWriteTimeUtc).ToList();
        PdfWindowLauncher.Start(Services.CommandLine.ComparePdfArgument, ordered);
        return Task.FromResult(new ActionResult([], []));
    }
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

/// <summary>Key sentences of a PDF in a small window beside the work, computed on the PC (no online service).</summary>
internal sealed class PdfSummaryAction : FileAction
{
    public override string Id => "pdf.summary";

    public override string Title => "Résumer";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.SummaryPdfArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Crop a picture by hand in a window: drag the frame, save a copy.</summary>
internal sealed class ImageCropAction : FileAction
{
    public override string Id => "image.crop";

    public override string Title => "Recadrer";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) =>
        Array.IndexOf(Views.ImageCropWindow.Extensions, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) >= 0;

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.CropImageArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Black boxes over sensitive text or images; the covered content is removed from the copy, not just hidden.</summary>
internal sealed class PdfRedactAction : FileAction
{
    public override string Id => "pdf.redact";

    public override string Title => "Caviarder";

    public override string Glyph => "";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.RedactPdfArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Watermark and crop of a PDF with a live preview of the first page, before anything is written.</summary>
internal sealed class PdfPreviewAction(string id, string title, string glyph) : FileAction
{
    public override string Id => id;

    public override string Title => title;

    public override string Glyph => glyph;

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => Raster.IsPdf(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.PreviewPdfPrefix + id, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Rename the selected files from a template (name, number, date), with a list of the new names before applying.</summary>
internal sealed class BulkRenameAction : FileAction
{
    public override string Id => "files.rename";

    public override string Title => "Renommer en masse";

    public override string Glyph => "\uE8AC";

    public override ActionGroup Group => ActionGroup.Tools;

    public override int MinInputs => 2;

    public override bool InProcess => true;

    public override bool Accepts(string path) => File.Exists(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(Services.CommandLine.RenameFilesArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>Arrows, boxes and text on a picture, or blur / hide a zone, in one editing window.</summary>
internal sealed class ImageMarkupAction(bool blur) : FileAction
{
    public override string Id => blur ? "image.blur" : "image.annotate";

    public override string Title => blur ? "Flouter / masquer" : "Annoter";

    public override string Glyph => blur ? "\uED1A" : "\uE7C6";

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) =>
        Array.IndexOf(Views.ImageCropWindow.Extensions, Path.GetExtension(path).TrimStart('.').ToLowerInvariant()) >= 0;

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(blur ? Services.CommandLine.BlurImageArgument : Services.CommandLine.AnnotateImageArgument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}

/// <summary>An action that opens a tool window of its own on one file or folder.</summary>
internal sealed class WindowAction(string id, string title, string glyph, string argument, Func<string, bool> accepts) : FileAction
{
    public override string Id => id;

    public override string Title => title;

    public override string Glyph => glyph;

    public override ActionGroup Group => ActionGroup.Tools;

    public override bool InProcess => true;

    public override bool IsAvailableFor(IReadOnlyList<string> inputs) => inputs.Count == 1 && Accepts(inputs[0]);

    public override bool Accepts(string path) => accepts(path);

    public override Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs, ActionContext context, IProgress<ActionProgress>? progress, CancellationToken cancellationToken)
    {
        PdfWindowLauncher.Start(argument, inputs);
        return Task.FromResult(new ActionResult([], []));
    }
}
