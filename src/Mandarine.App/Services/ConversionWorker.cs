using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mandarine.App.Interop;
using Mandarine.Core;
using Mandarine.Core.Actions;

namespace Mandarine.App.Services;

/// <summary>
/// Runs each conversion in a separate <c>Mandarine.exe --worker</c> process. Magick.NET, pdfium and
/// Skia are native code: a crash there must not take down the tray app and its mouse hook, and the
/// native memory they commit goes back to Windows when the worker exits.
/// Protocol: one <see cref="WorkerRequest"/> as JSON on stdin, then <see cref="WorkerMessage"/> JSON lines on stdout.
/// </summary>
internal static class ConversionWorker
{
    public const string Argument = "--worker";

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    internal sealed record WorkerRequest(
        string ActionId,
        IReadOnlyList<string> Files,
        string? OutputDirectory,
        int Quality,
        Dictionary<ExternalTool, string?> Tools);

    internal sealed record WorkerMessage(
        ActionProgress? Progress = null,
        ActionResult? Result = null,
        ExternalTool? MissingTool = null);

    // ---- Tray side ----

    /// <summary>Same contract as <see cref="FileAction.RunAsync"/>, out of process.</summary>
    /// <exception cref="OperationCanceledException">The worker was killed on request.</exception>
    /// <exception cref="MissingToolException">The action needs a tool that is not installed.</exception>
    public static async Task<ActionResult> RunAsync(
        FileAction action, IReadOnlyList<string> files, ActionContext context, IProgress<ActionProgress> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var request = new WorkerRequest(action.Id, files, context.OutputDirectory, context.Quality, new()
        {
            [ExternalTool.Ffmpeg] = context.Tools.Find(ExternalTool.Ffmpeg),
            [ExternalTool.LibreOffice] = context.Tools.Find(ExternalTool.LibreOffice),
        });

        var start = new ProcessStartInfo(Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Mandarine.exe"), Argument)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Le processus de conversion n'a pas démarré.");
        NativeMethods.AddToWorkerJob(process.Handle);
        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Already gone: the exit code below tells what happened.
        }

        await using var kill = token.Register(() => Kill(process));
        await process.StandardInput.WriteAsync(JsonSerializer.Serialize(request, Json));
        process.StandardInput.Close();

        WorkerMessage? final = null;
        while (await process.StandardOutput.ReadLineAsync(CancellationToken.None) is { } line)
        {
            WorkerMessage? message;
            try
            {
                message = JsonSerializer.Deserialize<WorkerMessage>(line, Json);
            }
            catch (JsonException)
            {
                continue;
            }

            if (message?.Progress is { } p)
            {
                progress.Report(p);
            }
            else if (message is not null)
            {
                final = message;
            }
        }

        await process.WaitForExitAsync(CancellationToken.None);
        token.ThrowIfCancellationRequested();

        if (final?.MissingTool is { } tool)
        {
            throw new MissingToolException(tool);
        }

        if (final?.Result is { } result)
        {
            return result;
        }

        // No answer: the worker crashed (access violation, stack overflow, out of memory) or was killed.
        return new ActionResult([], [new ActionError(files[0],
            $"La conversion s'est arrêtée de façon inattendue (code 0x{process.ExitCode:X8}). Mandarine continue de fonctionner.")]);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    // ---- Worker side ----

    /// <summary>Entry point of <c>Mandarine.exe --worker</c>: no window, no hook, no tray.</summary>
    public static int Run()
    {
        using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8) { AutoFlush = true };
        var gate = new object();
        void Send(WorkerMessage message)
        {
            var line = JsonSerializer.Serialize(message, Json);
            lock (gate)
            {
                output.WriteLine(line);
            }
        }

        WorkerRequest? request = null;
        try
        {
            string input;
            using (var reader = new StreamReader(Console.OpenStandardInput(), Utf8))
            {
                input = reader.ReadToEnd();
            }

            request = JsonSerializer.Deserialize<WorkerRequest>(input, Json) ?? throw new InvalidDataException("Requête vide.");
            var action = Mandarine.App.Actions.WindowsActions.Catalog().Find(request.ActionId) ?? throw new InvalidDataException($"Action inconnue : {request.ActionId}");
            var context = new ActionContext
            {
                OutputDirectory = request.OutputDirectory,
                Quality = request.Quality,
                Tools = new ToolLocator(request.Tools),
            };

            var files = request.Files;
            var progress = new SynchronousProgress(p => Send(new WorkerMessage(Progress: p)));
            var result = Task.Run(() => action.RunAsync(files, context, progress, CancellationToken.None)).GetAwaiter().GetResult();
            Send(new WorkerMessage(Result: result));
            return 0;
        }
        catch (MissingToolException ex)
        {
            Send(new WorkerMessage(MissingTool: ex.Tool));
            return 0;
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            Send(new WorkerMessage(Result: new ActionResult([], [new ActionError(request?.Files.FirstOrDefault() ?? string.Empty, ex.Message)])));
            return 0;
        }
    }

    /// <summary>Reports on the calling thread: there is no UI context to marshal to in the worker.</summary>
    private sealed class SynchronousProgress(Action<ActionProgress> handler) : IProgress<ActionProgress>
    {
        public void Report(ActionProgress value) => handler(value);
    }
}
