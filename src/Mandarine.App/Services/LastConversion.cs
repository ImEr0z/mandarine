using Microsoft.VisualBasic.FileIO;

namespace Mandarine.App.Services;

/// <summary>Remembers the outputs of the latest conversion so it can be undone (sent to the Recycle Bin).</summary>
internal static class LastConversion
{
    private static IReadOnlyList<string> _outputs = [];

    public static bool CanUndo => _outputs.Count > 0;

    public static void Remember(IReadOnlyList<string> outputs)
    {
        _outputs = outputs;
        History.Add(outputs);
    }

    /// <summary>Recycles every output that still exists. Returns how many were removed.</summary>
    public static int Undo()
    {
        var removed = 0;
        foreach (var path in _outputs)
        {
            try
            {
                if (File.Exists(path))
                {
                    FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    removed++;
                }
                else if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    removed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
            }
        }

        _outputs = [];
        return removed;
    }
}
