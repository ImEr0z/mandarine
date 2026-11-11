using System.Globalization;
using System.Text.RegularExpressions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using UglyToad.PdfPig.Content;

namespace Mandarine.Core;

/// <summary>A line of text with its place on the page. Units are PDF points, origin at the bottom left.</summary>
/// <param name="Y">Bottom of the box.</param>
/// <param name="Baseline">Where the glyphs sit: the text is drawn here.</param>
/// <param name="Color">0xRRGGBB.</param>
public sealed record PdfTextLine(
    int Page, string Text, double X, double Y, double Width, double Height, double Baseline,
    double FontSize, string Font, bool Bold, bool Italic, int Color);

/// <param name="Original">The line to hide, or null for text added on a blank spot.</param>
/// <param name="Replacement">The text to draw (its Text may be empty: the line is just erased).</param>
/// <param name="Background">0xRRGGBB of the page under the original, used to hide it.</param>
public sealed record PdfEdit(PdfTextLine? Original, PdfTextLine Replacement, int Background = 0xFFFFFF);

public sealed record PdfContent(IReadOnlyList<(double Width, double Height)> Pages, IReadOnlyList<PdfTextLine> Lines);

/// <summary>
/// Text editing without a PDF engine: lines are read with PdfPig, and a change is saved by painting the page
/// color over the old line and drawing the new text on top. The old text stays in the file, hidden.
/// </summary>
public static class PdfEditor
{
    public static PdfContent Read(string path)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var pages = new List<(double, double)>();
        var lines = new List<PdfTextLine>();
        foreach (var page in document.GetPages())
        {
            if (page.Rotation.Value != 0)
            {
                throw new InvalidOperationException("Les pages pivotées ne sont pas prises en charge. Utilisez d'abord « Pivoter les pages ».");
            }

            pages.Add((page.Width, page.Height));
            lines.AddRange(LinesOf(page));
        }

        return new PdfContent(pages, lines);
    }

    private static IEnumerable<PdfTextLine> LinesOf(Page page)
    {
        var words = page.GetWords().Where(w => w.Letters.Count > 0 && w.Text.Trim().Length > 0).ToList();
        var rows = new List<(double Baseline, List<Word> Words)>();
        foreach (var word in words.OrderByDescending(Baseline))
        {
            var tolerance = 0.4 * SizeOf(word);
            var row = rows.FindIndex(r => Math.Abs(r.Baseline - Baseline(word)) < tolerance);
            if (row < 0)
            {
                rows.Add((Baseline(word), [word]));
            }
            else
            {
                rows[row].Words.Add(word);
            }
        }

        foreach (var (baseline, rowWords) in rows)
        {
            var current = new List<Word>();
            foreach (var word in rowWords.OrderBy(w => w.BoundingBox.Left))
            {
                // A wide gap is another column, not a space.
                if (current.Count > 0 && word.BoundingBox.Left - current[^1].BoundingBox.Right > 1.2 * SizeOf(word))
                {
                    yield return Build(page.Number - 1, baseline, current);
                    current.Clear();
                }

                current.Add(word);
            }

            if (current.Count > 0)
            {
                yield return Build(page.Number - 1, baseline, current);
            }
        }
    }

    private static PdfTextLine Build(int page, double baseline, List<Word> words)
    {
        var first = words[0].Letters[0];
        var left = words.Min(w => w.BoundingBox.Left);
        var right = words.Max(w => w.BoundingBox.Right);
        var bottom = words.Min(w => w.BoundingBox.Bottom);
        var top = words.Max(w => w.BoundingBox.Top);
        var (family, bold, italic) = ParseFont(first.FontName);
        var (r, g, b) = first.Color.ToRGBValues();
        var color = ((int)Math.Round(r * 255) << 16) | ((int)Math.Round(g * 255) << 8) | (int)Math.Round(b * 255);
        return new PdfTextLine(page, string.Join(' ', words.Select(w => w.Text)), left, bottom, right - left, top - bottom,
            baseline, SizeOf(words[0]), family, bold, italic, color);
    }

    private static double Baseline(Word word) => word.Letters[0].StartBaseLine.Y;

    private static double SizeOf(Word word) => Math.Max(1, word.Letters[0].PointSize);

    /// <summary>"ABCDEF+Arial-BoldMT" becomes ("Arial", bold), "TimesNewRomanPSMT" becomes "Times New Roman".</summary>
    internal static (string Family, bool Bold, bool Italic) ParseFont(string? name)
    {
        name ??= string.Empty;
        name = name[(name.IndexOf('+') + 1)..];
        var style = name;
        var cut = name.IndexOfAny(['-', ',']);
        if (cut >= 0)
        {
            name = name[..cut];
        }

        name = Regex.Replace(name, "(PSMT|MT|PS)$", string.Empty);
        name = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
        var bold = Regex.IsMatch(style, "Bold|Black|Heavy", RegexOptions.IgnoreCase);
        var italic = Regex.IsMatch(style, "Italic|Oblique", RegexOptions.IgnoreCase);
        return (name.Length == 0 ? "Arial" : name, bold, italic);
    }

    /// <summary>Writes the edited copy. Saved aside then moved, so that the output may be the input file.</summary>
    public static void Save(string input, string output, IReadOnlyList<PdfEdit> edits, IReadOnlyList<PdfStamp>? stamps = null)
    {
        try
        {
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        }
        catch (InvalidOperationException)
        {
            // Already set earlier in this process.
        }

        PdfDocument document;
        try
        {
            document = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("Ce PDF ne peut pas être modifié (protégé par un mot de passe ou format non pris en charge).", ex);
        }

        // Two passes: PDFsharp drops a rewritten content stream when it then appends its own drawing,
        // so the cleaned document goes through memory and is reopened before drawing.
        var cleaned = false;
        foreach (var group in edits.GroupBy(e => e.Replacement.Page))
        {
            cleaned |= RemoveOriginalText(document.Pages[group.Key], group.Where(e => e.Original is not null).Select(e => e.Original!).ToList());
        }

        if (cleaned)
        {
            var memory = new MemoryStream();
            document.Save(memory, closeStream: false);
            document.Dispose();
            memory.Position = 0;
            document = PdfReader.Open(memory, PdfDocumentOpenMode.Modify);
        }

        var temporary = output + ".mandarine.tmp";
        using (document)
        {
            stamps ??= [];
            foreach (var pageIndex in edits.Select(e => e.Replacement.Page).Union(stamps.Select(s => s.Page)).Order())
            {
                var group = edits.Where(e => e.Replacement.Page == pageIndex);
                var page = document.Pages[pageIndex];
                var height = page.Height.Point;
                var originX = page.MediaBox.X1;
                var originY = page.MediaBox.Y1;
                using var graphics = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                foreach (var edit in group)
                {
                    if (edit.Original is { } o)
                    {
                        // A little margin: the glyph outlines go slightly past the measured box.
                        graphics.DrawRectangle(new XSolidBrush(ToColor(edit.Background)),
                            o.X - originX - 1, height - (o.Y - originY + o.Height) - 1, o.Width + 2, o.Height + 2);
                    }

                    var r = edit.Replacement;
                    if (r.Text.Length > 0)
                    {
                        graphics.DrawString(r.Text, CreateFont(r), new XSolidBrush(ToColor(r.Color)),
                            r.X - originX, height - (r.Baseline - originY), XStringFormats.BaseLineLeft);
                    }
                }

                foreach (var stamp in stamps.Where(s => s.Page == pageIndex))
                {
                    using var image = XImage.FromStream(new MemoryStream(stamp.Image));
                    graphics.DrawImage(image, stamp.X, stamp.Y, stamp.Width, stamp.Height);
                }
            }

            try
            {
                document.Save(temporary);
                File.Move(temporary, output, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }

    /// <summary>
    /// Drops the text-showing operators of the replaced lines from the page content, so that the old text is gone from the
    /// file (copy, search, redaction). The start of each text run is found by following the matrices; a run that continues
    /// the previous one has no position of its own and follows its decision. Text inside form XObjects is not reached,
    /// and a page this cannot parse is left as it is: the cover rectangle still hides the old text.
    /// </summary>
    private static bool RemoveOriginalText(PdfPage page, IReadOnlyList<PdfTextLine> lines)
    {
        if (lines.Count == 0)
        {
            return false;
        }

        try
        {
            var sequence = ContentReader.ReadContent(page);
            var kept = new CSequence();
            double[] ctm = [1, 0, 0, 1, 0, 0], tm = [1, 0, 0, 1, 0, 0], tlm = [1, 0, 0, 1, 0, 0];
            var stack = new Stack<double[]>();
            double leading = 0;
            var removing = false;
            var positioned = false;
            var removedAny = false;

            bool Inside()
            {
                var m = Multiply(tm, ctm);
                return lines.Any(l => Math.Abs(m[5] - l.Baseline) < 0.35 * Math.Max(l.Height, l.FontSize)
                    && m[4] >= l.X - 2 && m[4] <= l.X + l.Width);
            }

            foreach (var item in sequence)
            {
                if (item is not COperator op)
                {
                    kept.Add(item);
                    continue;
                }

                var n = op.Operands.Select(Number).ToArray();
                switch (op.OpCode.OpCodeName)
                {
                    case OpCodeName.q:
                        stack.Push(ctm);
                        break;
                    case OpCodeName.Q:
                        ctm = stack.Count > 0 ? stack.Pop() : ctm;
                        break;
                    case OpCodeName.cm when n.Length == 6:
                        ctm = Multiply(n, ctm);
                        break;
                    case OpCodeName.BT:
                        tm = tlm = [1, 0, 0, 1, 0, 0];
                        positioned = true;
                        break;
                    case OpCodeName.Tm when n.Length == 6:
                        tm = tlm = n;
                        positioned = true;
                        break;
                    case OpCodeName.Td when n.Length == 2:
                        tlm = tm = Multiply([1, 0, 0, 1, n[0], n[1]], tlm);
                        positioned = true;
                        break;
                    case OpCodeName.TD when n.Length == 2:
                        leading = -n[1];
                        tlm = tm = Multiply([1, 0, 0, 1, n[0], n[1]], tlm);
                        positioned = true;
                        break;
                    case OpCodeName.TL when n.Length == 1:
                        leading = n[0];
                        break;
                    case OpCodeName.Tx:
                        tlm = tm = Multiply([1, 0, 0, 1, 0, -leading], tlm);
                        positioned = true;
                        break;
                }

                var shows = op.OpCode.OpCodeName is OpCodeName.Tj or OpCodeName.TJ or OpCodeName.QuoteSingle or OpCodeName.QuoteDouble;
                if (shows)
                {
                    if (op.OpCode.OpCodeName is OpCodeName.QuoteSingle or OpCodeName.QuoteDouble)
                    {
                        tlm = tm = Multiply([1, 0, 0, 1, 0, -leading], tlm);
                        positioned = true;
                    }

                    if (positioned)
                    {
                        removing = Inside();
                    }

                    positioned = false;
                    if (removing)
                    {
                        removedAny = true;
                        continue;
                    }
                }

                kept.Add(op);
            }

            if (removedAny)
            {
                var bytes = System.Text.Encoding.Latin1.GetBytes(Serialize(kept));
                var content = new PdfDictionary(page.Owner);
                content.CreateStream(bytes);
                page.Owner.Internals.AddObject(content);
                page.Elements.SetReference("/Contents", content);
            }

            return removedAny;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unusual content: keep the original text under the cover.
            return false;
        }
    }

    /// <summary>Content stream text for the operators kept. Anything unexpected throws: the page is then left untouched.</summary>
    private static string Serialize(CSequence sequence)
    {
        var text = new System.Text.StringBuilder();
        foreach (var item in sequence)
        {
            if (item is not COperator op)
            {
                throw new NotSupportedException();
            }

            foreach (var operand in op.Operands)
            {
                WriteOperand(text, operand);
                text.Append(' ');
            }

            text.Append(op.OpCode.Name).Append('\n');
        }

        return text.ToString();
    }

    private static void WriteOperand(System.Text.StringBuilder text, CObject operand)
    {
        var invariant = CultureInfo.InvariantCulture;
        switch (operand)
        {
            case CInteger i:
                text.Append(i.Value.ToString(invariant));
                break;
            case CReal r:
                text.Append(r.Value.ToString("0.##########", invariant));
                break;
            case CName n:
                text.Append(n.Name);
                break;
            case CString s:
                text.Append('(');
                foreach (var c in s.Value)
                {
                    if (c is '\\' or '(' or ')')
                    {
                        text.Append('\\').Append(c);
                    }
                    else if (c is < ' ' or > '~')
                    {
                        text.Append('\\').Append(Convert.ToString(c & 0xFF, 8).PadLeft(3, '0'));
                    }
                    else
                    {
                        text.Append(c);
                    }
                }

                text.Append(')');
                break;
            case CArray a:
                text.Append('[');
                foreach (var element in a)
                {
                    WriteOperand(text, element);
                    text.Append(' ');
                }

                text.Append(']');
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private static double Number(CObject value) => value switch
    {
        CInteger i => i.Value,
        CReal r => r.Value,
        _ => double.NaN,
    };

    /// <summary>PDF matrices [a b c d e f]: the result applies <paramref name="first"/>, then <paramref name="second"/>.</summary>
    private static double[] Multiply(double[] first, double[] second) =>
    [
        first[0] * second[0] + first[1] * second[2],
        first[0] * second[1] + first[1] * second[3],
        first[2] * second[0] + first[3] * second[2],
        first[2] * second[1] + first[3] * second[3],
        first[4] * second[0] + first[5] * second[2] + second[4],
        first[4] * second[1] + first[5] * second[3] + second[5],
    ];

    private static XColor ToColor(int rgb) => XColor.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    private static XFont CreateFont(PdfTextLine line)
    {
        var style = (line.Bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) | (line.Italic ? XFontStyleEx.Italic : XFontStyleEx.Regular);
        try
        {
            return new XFont(line.Font, line.FontSize, style);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            // Font not installed on this PC: the closest common one.
            return new XFont("Arial", line.FontSize, style);
        }
    }
}

/// <summary>An image (a signature) placed on a page; X and Y are the top-left corner in points from the top-left of the page.</summary>
public sealed record PdfStamp(int Page, double X, double Y, double Width, double Height, byte[] Image);
