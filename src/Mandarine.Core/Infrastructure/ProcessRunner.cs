using System.Diagnostics;

namespace Mandarine.Core;

public sealed class ExternalToolException(string message) : Exception(message);

public static class ProcessRunner
{
    /// <summary>
    /// Runs a command-line tool without a console window, streaming its output to the callbacks.
    /// Throws <see cref="ExternalToolException"/> with the tail of stderr when the exit code is not zero.
    /// </summary>
    public static async Task RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken,
        Action<string>? onOutput = null,
        Action<string>? onError = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var errorTail = new Queue<string>();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                onOutput?.Invoke(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            onError?.Invoke(e.Data);
            lock (errorTail)
            {
                errorTail.Enqueue(e.Data);
                if (errorTail.Count > 12)
                {
                    errorTail.Dequeue();
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            throw;
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (errorTail)
            {
                detail = string.Join(Environment.NewLine, errorTail.Where(l => !string.IsNullOrWhiteSpace(l)));
            }

            var name = Path.GetFileNameWithoutExtension(executable);
            throw new ExternalToolException(string.IsNullOrEmpty(detail)
                ? $"{name} a échoué (code {process.ExitCode})."
                : $"{name} a échoué : {detail}");
        }
    }
}
