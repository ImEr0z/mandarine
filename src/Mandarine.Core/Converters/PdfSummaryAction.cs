using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Mandarine.Core.Converters;

/// <summary>
/// Summary without any AI service: TextRank. Sentences are ranked by how many significant words they share with
/// the others (the same idea as PageRank), and the best ones are kept in their original order. It selects
/// sentences of the document, it never writes new ones.
/// </summary>
public static partial class TextSummary
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "le", "la", "les", "un", "une", "des", "du", "de", "et", "ou", "mais", "donc", "que", "qui", "quoi", "dans", "pour",
        "par", "sur", "avec", "sans", "sous", "est", "sont", "été", "être", "avoir", "ont", "cette", "ces", "son", "sa",
        "ses", "leur", "leurs", "nous", "vous", "ils", "elles", "pas", "plus", "aux", "ainsi", "comme", "tout", "tous",
        "the", "and", "for", "are", "with", "that", "this", "from", "have", "has", "was", "were", "not", "but", "you",
        "your", "their", "will", "can", "all", "any", "its", "into", "than", "then", "which",
    };

    public static IReadOnlyList<string> Summarize(string text, int maxSentences = 0)
    {
        var sentences = SentenceBreak().Split(text.Replace('\r', ' '))
            .Select(s => WhiteSpace().Replace(s, " ").Trim())
            .Where(s => s.Length is >= 25 and <= 500)
            .ToList();
        if (sentences.Count <= 3)
        {
            return sentences;
        }

        var words = sentences.Select(Significant).ToList();
        var count = sentences.Count;
        var weight = new double[count, count];
        var total = new double[count];
        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (words[i].Count < 2 || words[j].Count < 2)
                {
                    continue;
                }

                var shared = words[i].Count(words[j].Contains);
                var similarity = shared / (Math.Log(words[i].Count + 1) + Math.Log(words[j].Count + 1));
                weight[i, j] = weight[j, i] = similarity;
                total[i] += similarity;
                total[j] += similarity;
            }
        }

        var score = Enumerable.Repeat(1.0, count).ToArray();
        for (var round = 0; round < 30; round++)
        {
            var next = new double[count];
            for (var i = 0; i < count; i++)
            {
                var sum = 0.0;
                for (var j = 0; j < count; j++)
                {
                    if (weight[j, i] > 0)
                    {
                        sum += weight[j, i] / total[j] * score[j];
                    }
                }

                next[i] = 0.15 + 0.85 * sum;
            }

            score = next;
        }

        var keep = maxSentences > 0 ? maxSentences : Math.Clamp(count / 10, 5, 15);
        return Enumerable.Range(0, count).OrderByDescending(i => score[i]).Take(keep).Order().Select(i => sentences[i]).ToList();
    }

    /// <summary>The key sentences of a PDF's text, in reading order. Empty for a scanned document.</summary>
    public static IReadOnlyList<string> SummarizePdf(string path, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            text.AppendLine(ContentOrderTextExtractor.GetText(page));
        }

        return Summarize(text.ToString());
    }

    private static HashSet<string> Significant(string sentence) =>
        Word().Matches(sentence).Select(m => m.Value.ToLowerInvariant()).Where(w => w.Length >= 3 && !StopWords.Contains(w)).ToHashSet();

    [GeneratedRegex(@"(?<=[.!?…])\s+|\n{2,}")]
    private static partial Regex SentenceBreak();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Word();
}
