using Mandarine.Core.Converters;

namespace Mandarine.Core.Actions;

/// <summary>Every action Mandarine knows, in display order.</summary>
public sealed class ActionCatalog
{
    // Order = order on the wheel: the most useful targets first (8 slices before "Plus…").
    public static readonly IReadOnlyList<string> ImageTargets = ["png", "jpg", "pdf", "webp", "avif", "gif", "bmp", "tiff", "ico", "jxl"];

    public ActionCatalog()
        : this(BuildDefault())
    {
    }

    public ActionCatalog(IEnumerable<FileAction> actions)
    {
        All = actions.ToList();
        var duplicate = All.GroupBy(a => a.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Action en double : {duplicate.Key}");
        }
    }

    public IReadOnlyList<FileAction> All { get; }

    /// <summary>Actions applicable to the whole selection: conversions first, then tools.</summary>
    public IReadOnlyList<FileAction> For(IReadOnlyList<string> inputs)
    {
        if (inputs.Count == 0)
        {
            return [];
        }

        using var _ = FileKinds.CacheScope();
        return All.Where(a => a.IsAvailableFor(inputs)).OrderBy(a => a.Group).ToList();
    }

    public FileAction? Find(string id) => All.FirstOrDefault(a => a.Id == id);

    private static IEnumerable<FileAction> BuildDefault()
    {
        foreach (var ext in ImageTargets)
        {
            yield return new ImageConvertAction(ext);
        }

        yield return new PdfToImagesAction("png");
        yield return new PdfToImagesAction("jpg");
        yield return new PdfToTextAction();

        foreach (var action in MediaActions.All()
                     .Concat(OfficeConvertAction.All())
                     .Concat(MarkupActions.All())
                     .Concat(DataActions.All())
                     .Concat(ImageToolAction.All()))
        {
            yield return action;
        }

        yield return new ImagesToPdfAction();
        yield return new ImageDataUriAction();
        yield return new PdfMergeAction();
        yield return new PdfSplitAction();
        yield return new PdfRotateAction();
        yield return new ExtractAction();
        yield return new ZipAction();
        yield return new HashAction();
        yield return new CopyPathAction();
    }
}
