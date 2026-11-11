using System.Text.Json;

namespace Mandarine.App.Services;

internal sealed record HistoryEntry(DateTime Time, string Path);

/// <summary>The latest files Mandarine created, kept between sessions so they can be found again from the menu.</summary>
internal static class History
{
    private const int Capacity = 40;
    private const int PerConversion = 10;

    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mandarine", "history.json");

    public static void Add(IReadOnlyList<string> outputs)
    {
        var now = DateTime.Now;
        var entries = outputs.Take(PerConversion).Select(p => new HistoryEntry(now, p))
            .Concat(Read()).Take(Capacity).ToList();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The history is a convenience: losing it must never fail a conversion.
        }
    }

    /// <summary>Newest first, without the files that were deleted or moved since.</summary>
    public static IReadOnlyList<HistoryEntry> Load() =>
        Read().Where(e => File.Exists(e.Path) || Directory.Exists(e.Path)).ToList();

    private static IEnumerable<HistoryEntry> Read()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }
}
