using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Mandarine.Core;

/// <summary>One line that is only in the old version (<see cref="Added"/> false) or only in the new one.</summary>
public sealed record PdfDifference(int Page, bool Added, string Text);

public sealed record PdfComparison(int OldPages, int NewPages, IReadOnlyList<PdfDifference> Differences);

/// <summary>Compares the text of two PDF versions page by page, line by line.</summary>
public static class PdfCompare
{
    public static PdfComparison Compare(string oldPath, string newPath)
    {
        var before = Lines(oldPath);
        var after = Lines(newPath);
        var differences = new List<PdfDifference>();
        for (var page = 0; page < Math.Max(before.Count, after.Count); page++)
        {
            Diff(page + 1, page < before.Count ? before[page] : [], page < after.Count ? after[page] : [], differences);
        }

        return new PdfComparison(before.Count, after.Count, differences);
    }

    /// <summary>Longest common subsequence of lines: what is not in it was removed (old side) or added (new side).</summary>
    internal static void Diff(int page, IReadOnlyList<string> before, IReadOnlyList<string> after, List<PdfDifference> result)
    {
        var table = new int[before.Count + 1, after.Count + 1];
        for (var i = before.Count - 1; i >= 0; i--)
        {
            for (var j = after.Count - 1; j >= 0; j--)
            {
                table[i, j] = before[i] == after[j] ? table[i + 1, j + 1] + 1 : Math.Max(table[i + 1, j], table[i, j + 1]);
            }
        }

        int a = 0, b = 0;
        while (a < before.Count || b < after.Count)
        {
            if (a < before.Count && b < after.Count && before[a] == after[b])
            {
                a++;
                b++;
            }
            else if (b >= after.Count || (a < before.Count && table[a + 1, b] >= table[a, b + 1]))
            {
                result.Add(new PdfDifference(page, false, before[a++]));
            }
            else
            {
                result.Add(new PdfDifference(page, true, after[b++]));
            }
        }
    }

    private static List<List<string>> Lines(string path)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        return document.GetPages()
            .Select(p => ContentOrderTextExtractor.GetText(p)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList())
            .ToList();
    }
}
