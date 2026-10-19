namespace Mandarine.Core;

public enum FileKind
{
    Other,
    Folder,
    Image,
    Audio,
    Video,
    Pdf,
    WordDocument,
    Spreadsheet,
    Presentation,
    Text,
    Markdown,
    Html,
    Json,
    Yaml,
    Xml,
    Csv,
    Archive,
}

public static class FileKinds
{
    private static readonly Dictionary<string, FileKind> ByExtension = Build();

    private static Dictionary<string, FileKind> Build()
    {
        var map = new Dictionary<string, FileKind>(StringComparer.OrdinalIgnoreCase);

        void Add(FileKind kind, params string[] extensions)
        {
            foreach (var ext in extensions)
            {
                map[ext] = kind;
            }
        }

        Add(FileKind.Image,
            "png", "jpg", "jpeg", "jpe", "jfif", "webp", "avif", "heic", "heif", "gif", "bmp", "dib",
            "tif", "tiff", "ico", "tga", "psd", "svg", "jxl", "jp2",
            "dng", "cr2", "cr3", "nef", "arw", "raf", "orf", "rw2", "pef", "srw");
        Add(FileKind.Audio,
            "mp3", "wav", "flac", "aac", "m4a", "ogg", "oga", "opus", "aiff", "aif", "aifc", "wma", "amr",
            "ac3", "caf", "mka");
        Add(FileKind.Video,
            "mp4", "m4v", "mov", "mkv", "webm", "avi", "wmv", "flv", "mpg", "mpeg", "3gp", "ts", "mts",
            "m2ts", "vob", "ogv");
        Add(FileKind.Pdf, "pdf");
        Add(FileKind.WordDocument, "doc", "docx", "docm", "dotx", "odt", "rtf", "wpd");
        Add(FileKind.Spreadsheet, "xls", "xlsx", "xlsm", "ods");
        Add(FileKind.Presentation, "ppt", "pptx", "pps", "ppsx", "odp");
        Add(FileKind.Text, "txt", "log", "text");
        Add(FileKind.Markdown, "md", "markdown", "mdown");
        Add(FileKind.Html, "html", "htm", "xhtml");
        Add(FileKind.Json, "json");
        Add(FileKind.Yaml, "yaml", "yml");
        Add(FileKind.Xml, "xml");
        Add(FileKind.Csv, "csv", "tsv");
        Add(FileKind.Archive, "zip", "tar", "tgz", "gz", "7z", "rar");
        return map;
    }

    /// <summary>Lower-case extension without the leading dot ("tar.gz" is reported as "gz").</summary>
    public static string ExtensionOf(string path) =>
        Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

    public static FileKind Detect(string path)
    {
        if (Directory.Exists(path))
        {
            return FileKind.Folder;
        }

        return ByExtension.TryGetValue(ExtensionOf(path), out var kind) ? kind : FileKind.Other;
    }

    public static string Label(FileKind kind) => kind switch
    {
        FileKind.Folder => "Dossier",
        FileKind.Image => "Image",
        FileKind.Audio => "Audio",
        FileKind.Video => "Vidéo",
        FileKind.Pdf => "PDF",
        FileKind.WordDocument => "Document",
        FileKind.Spreadsheet => "Tableur",
        FileKind.Presentation => "Présentation",
        FileKind.Text => "Texte",
        FileKind.Markdown => "Markdown",
        FileKind.Html => "HTML",
        FileKind.Json => "JSON",
        FileKind.Yaml => "YAML",
        FileKind.Xml => "XML",
        FileKind.Csv => "CSV",
        FileKind.Archive => "Archive",
        _ => "Fichier",
    };

    /// <summary>Short human description of a selection, e.g. "Image HEIC" or "3 vidéos".</summary>
    public static string Describe(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return string.Empty;
        }

        var kinds = paths.Select(Detect).Distinct().ToList();
        if (paths.Count == 1)
        {
            var kind = kinds[0];
            var ext = ExtensionOf(paths[0]).ToUpperInvariant();
            return kind is FileKind.Folder or FileKind.Other || ext.Length == 0
                ? Label(kind)
                : $"{Label(kind)} {ext}";
        }

        if (kinds.Count == 1)
        {
            return kinds[0] switch
            {
                FileKind.Folder => $"{paths.Count} dossiers",
                FileKind.Image => $"{paths.Count} images",
                FileKind.Audio => $"{paths.Count} fichiers audio",
                FileKind.Video => $"{paths.Count} vidéos",
                FileKind.Pdf => $"{paths.Count} PDF",
                FileKind.WordDocument => $"{paths.Count} documents",
                _ => $"{paths.Count} fichiers {Label(kinds[0])}",
            };
        }

        return $"{paths.Count} éléments";
    }
}
