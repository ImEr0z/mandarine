using System.Globalization;
using System.Text.RegularExpressions;

namespace Mandarine.Core;

/// <param name="Template">Free text with {nom} (current name), {n} (number) and {date} (last modified, yyyy-MM-dd).</param>
public sealed record RenameOptions(string Template, int Start = 1, int Digits = 3);

/// <param name="Problem">Why this file cannot take its new name (empty name, duplicate, already exists), or null.</param>
public sealed record RenamePlan(string From, string To, string? Problem)
{
    public bool Changes => !string.Equals(From, To, StringComparison.Ordinal);
}

/// <summary>Renames several files at once from a template. The extension is always kept.</summary>
public static partial class BulkRename
{
    private static readonly StringComparer Natural = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.NumericOrdering | CompareOptions.IgnoreCase);

    public static IReadOnlyList<RenamePlan> Plan(IReadOnlyList<string> files, RenameOptions options)
    {
        var ordered = files.OrderBy(Path.GetFileName, Natural).ToList();
        var plans = new List<RenamePlan>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var file = ordered[i];
            var name = Path.GetFileNameWithoutExtension(file);
            var number = (options.Start + i).ToString(CultureInfo.InvariantCulture).PadLeft(Math.Clamp(options.Digits, 1, 9), '0');
            var date = File.GetLastWriteTime(file).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var text = Token().Replace(options.Template, m => m.Value.ToLowerInvariant() switch
            {
                "{nom}" => name,
                "{n}" => number,
                _ => date,
            });
            text = string.Concat(text.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim().TrimEnd('.');
            var target = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, text + Path.GetExtension(file));
            plans.Add(new RenamePlan(file, target, text.Length == 0 ? "Nom vide" : null));
        }

        var duplicates = plans.GroupBy(p => p.To, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = plans.Select(p => p.From).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return plans.Select(p => p.Problem is not null ? p
            : duplicates.Contains(p.To) ? p with { Problem = "Deux fichiers auraient le même nom" }
            : !sources.Contains(p.To) && (File.Exists(p.To) || Directory.Exists(p.To)) ? p with { Problem = "Un fichier porte déjà ce nom" }
            : p).ToList();
    }

    /// <summary>
    /// Renames through temporary names, so that swaps and chains ("a" to "b" while "b" to "c") never overwrite anything.
    /// Returns what was renamed, to give to <see cref="Undo"/>.
    /// </summary>
    public static IReadOnlyList<(string From, string To)> Apply(IReadOnlyList<RenamePlan> plans)
    {
        if (plans.Any(p => p.Problem is not null))
        {
            throw new InvalidOperationException("Corrigez d'abord les noms en conflit.");
        }

        var moves = plans.Where(p => p.Changes).Select(p => (p.From, p.To)).ToList();
        return Move(moves);
    }

    public static void Undo(IReadOnlyList<(string From, string To)> applied) => Move(applied.Select(m => (From: m.To, To: m.From)).ToList());

    private static IReadOnlyList<(string From, string To)> Move(List<(string From, string To)> moves)
    {
        var temporary = new List<(string Temp, string From, string To)>();
        try
        {
            foreach (var (from, to) in moves)
            {
                var temp = Path.Combine(Path.GetDirectoryName(from)!, $".mandarine-rename-{Guid.NewGuid():N}{Path.GetExtension(from)}");
                File.Move(from, temp);
                temporary.Add((temp, from, to));
            }

            foreach (var (temp, _, to) in temporary)
            {
                File.Move(temp, to);
            }
        }
        catch
        {
            // Put everything back where it was.
            foreach (var (temp, from, to) in temporary)
            {
                try
                {
                    File.Move(File.Exists(temp) ? temp : to, from);
                }
                catch (IOException)
                {
                }
            }

            throw;
        }

        return moves;
    }

    [GeneratedRegex(@"\{(nom|n|date)\}", RegexOptions.IgnoreCase)]
    private static partial Regex Token();
}
