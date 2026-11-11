using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mandarine.Core;

public sealed record PdfInfo(
    string Title, string Author, string Subject, string Keywords, string Creator, string Producer,
    DateTime? Created, DateTime? Modified, int Bookmarks, bool HasXmp);

/// <param name="StripHidden">Remove the program, the producer, the dates and the XMP block (what a PDF says about how it was made).</param>
/// <param name="StripBookmarks">Remove the bookmarks (outline).</param>
public sealed record PdfInfoEdit(string Title, string Author, string Subject, string Keywords, bool StripHidden, bool StripBookmarks);

/// <summary>Reads and rewrites the document information of a PDF (title, author...), and removes what is not meant to be shared.</summary>
public static class PdfMetadata
{
    public static PdfInfo Read(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        var info = document.Info;
        return new PdfInfo(info.Title, info.Author, info.Subject, info.Keywords, info.Creator, info.Producer,
            Valid(info.CreationDate), Valid(info.ModificationDate), document.Outlines.Count, HasXmpPacket(path));
    }

    public static void Save(string input, string output, PdfInfoEdit edit)
    {
        using var document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        document.Info.Title = edit.Title;
        document.Info.Author = edit.Author;
        document.Info.Subject = edit.Subject;
        document.Info.Keywords = edit.Keywords;
        if (edit.StripHidden)
        {
            foreach (var key in new[] { "/Creator", "/Producer", "/CreationDate", "/ModDate" })
            {
                document.Info.Elements.Remove(key);
            }

            document.Internals.Catalog.Elements.Remove("/Metadata");

            // PDFsharp writes its own name in the empty fields when saving: a blank value keeps them empty.
            document.Info.Creator = " ";
            document.Info.Elements.SetString("/Producer", " ");
        }

        if (edit.StripBookmarks)
        {
            document.Outlines.Clear();
        }

        document.Save(output);
    }

    /// <summary>An XMP block is stored as a plain "xpacket" stream: looking for it in the file is enough to know if there is one.</summary>
    private static bool HasXmpPacket(string path) =>
        System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(path)).Contains("<?xpacket", StringComparison.Ordinal);

    private static DateTime? Valid(DateTime date) => date.Year > 1970 ? date : null;
}
