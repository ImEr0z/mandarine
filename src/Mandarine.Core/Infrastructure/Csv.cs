using System.Text;

namespace Mandarine.Core;

/// <summary>Minimal RFC 4180 reader/writer with delimiter detection (French Excel exports use ";").</summary>
public static class Csv
{
    public static char DetectDelimiter(string text, string? path = null)
    {
        if (path is not null && Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase))
        {
            return '\t';
        }

        var end = text.AsSpan().IndexOfAny('\r', '\n');
        var firstLine = end >= 0 ? text[..end] : text;

        char[] candidates = [',', ';', '\t', '|'];
        return candidates.OrderByDescending(c => CountOutsideQuotes(firstLine, c)).First();
    }

    private static int CountOutsideQuotes(string line, char delimiter)
    {
        var count = 0;
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == delimiter && !quoted)
            {
                count++;
            }
        }

        return count;
    }

    public static List<string[]> Parse(string text, char delimiter)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                rows.Add([.. row]);
                row.Clear();
            }
            else if (c != '﻿' || i != 0)
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add([.. row]);
        }

        rows.RemoveAll(r => r.Length == 1 && r[0].Length == 0);
        return rows;
    }

    public static string Write(IEnumerable<IReadOnlyList<string>> rows, char delimiter)
    {
        var builder = new StringBuilder();
        foreach (var row in rows)
        {
            builder.AppendJoin(delimiter, row.Select(value => Escape(value, delimiter)));
            builder.Append("\r\n");
        }

        return builder.ToString();
    }

    private static string Escape(string value, char delimiter) =>
        value.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
