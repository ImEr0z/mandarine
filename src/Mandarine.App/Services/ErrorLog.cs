namespace Mandarine.App.Services;

/// <summary>
/// %LocalAppData%\Mandarine\errors.log, capped at 1 MB (the previous file is kept as errors.old.log).
/// Exceptions only: no file content, no keystrokes, nothing typed by the user.
/// </summary>
internal static class ErrorLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static void Write(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarine");
                Directory.CreateDirectory(folder);
                var file = Path.Combine(folder, "errors.log");
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    File.Move(file, Path.Combine(folder, "errors.old.log"), overwrite: true);
                }

                File.AppendAllText(file, $"[{DateTime.Now:O}] {exception}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Fire-and-forget without losing failures: anything thrown ends up in the log.</summary>
    public static async void Forget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            Write(ex);
        }
    }
}
