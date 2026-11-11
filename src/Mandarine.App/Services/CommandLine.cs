namespace Mandarine.App.Services;

/// <summary>
/// Mandarine.exe [--background] [--settings] [files...]
/// Mandarine.exe --worker runs one conversion for the tray app (see ConversionWorker) and exits.
/// Files are what Explorer passes from the context menu or "Send to".
/// </summary>
internal sealed record CommandLine(IReadOnlyList<string> Files, bool Background, bool Settings, bool Worker = false, bool EditPdf = false, bool OrganizePdf = false, bool ComparePdf = false, bool SummaryPdf = false, bool CropImage = false, bool RedactPdf = false, bool RenameFiles = false, bool AnnotateImage = false, bool BlurImage = false, string? PreviewAction = null, bool MetadataPdf = false, bool SortFolder = false, bool FindDuplicates = false, bool AdjustImage = false)
{
    /// <summary>Mandarine.exe --edit-pdf file.pdf opens the PDF text editor alone, in its own process.</summary>
    public const string EditPdfArgument = "--edit-pdf";
    public const string OrganizePdfArgument = "--organize-pdf";
    public const string ComparePdfArgument = "--compare-pdf";
    public const string SummaryPdfArgument = "--summary-pdf";
    public const string CropImageArgument = "--crop-image";
    public const string RedactPdfArgument = "--redact-pdf";
    public const string RenameFilesArgument = "--rename-files";
    public const string AnnotateImageArgument = "--annotate-image";
    public const string BlurImageArgument = "--blur-image";
    public const string MetadataPdfArgument = "--metadata-pdf";
    public const string SortFolderArgument = "--sort-folder";
    public const string FindDuplicatesArgument = "--find-duplicates";
    public const string AdjustImageArgument = "--adjust-image";

    /// <summary>--preview-pdf=pdf.watermark opens the preview window of that action on the given PDF.</summary>
    public const string PreviewPdfPrefix = "--preview-pdf=";

    /// <summary>Sent to the running tray app: --notify title text [path-to-open].</summary>
    public const string NotifyArgument = "--notify";

    public static CommandLine Parse(IEnumerable<string> arguments)
    {
        var files = new List<string>();
        bool background = false, settings = false, worker = false, editPdf = false, organizePdf = false, comparePdf = false, summaryPdf = false, cropImage = false, redactPdf = false, renameFiles = false, annotateImage = false, blurImage = false, metadataPdf = false, sortFolder = false, findDuplicates = false, adjustImage = false;
        string? previewAction = null;
        foreach (var argument in arguments)
        {
            switch (argument.ToLowerInvariant())
            {
                case "--background":
                    background = true;
                    break;
                case "--settings":
                    settings = true;
                    break;
                case ConversionWorker.Argument:
                    worker = true;
                    break;
                case EditPdfArgument:
                    editPdf = true;
                    break;
                case OrganizePdfArgument:
                    organizePdf = true;
                    break;
                case ComparePdfArgument:
                    comparePdf = true;
                    break;
                case SummaryPdfArgument:
                    summaryPdf = true;
                    break;
                case CropImageArgument:
                    cropImage = true;
                    break;
                case RedactPdfArgument:
                    redactPdf = true;
                    break;
                case RenameFilesArgument:
                    renameFiles = true;
                    break;
                case AnnotateImageArgument:
                    annotateImage = true;
                    break;
                case BlurImageArgument:
                    blurImage = true;
                    break;
                case MetadataPdfArgument:
                    metadataPdf = true;
                    break;
                case SortFolderArgument:
                    sortFolder = true;
                    break;
                case FindDuplicatesArgument:
                    findDuplicates = true;
                    break;
                case AdjustImageArgument:
                    adjustImage = true;
                    break;
                default:
                    if (argument.StartsWith(PreviewPdfPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        previewAction = argument[PreviewPdfPrefix.Length..];
                    }
                    else if (!argument.StartsWith("--", StringComparison.Ordinal) && (File.Exists(argument) || Directory.Exists(argument)))
                    {
                        files.Add(Path.GetFullPath(argument));
                    }

                    break;
            }
        }

        return new CommandLine(files, background, settings, worker, editPdf, organizePdf, comparePdf, summaryPdf, cropImage, redactPdf, renameFiles, annotateImage, blurImage, previewAction, metadataPdf, sortFolder, findDuplicates, adjustImage);
    }
}
