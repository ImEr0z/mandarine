namespace Mandarine.Core.Actions;

public enum ActionGroup
{
    /// <summary>Format conversions, rendered as large format tiles.</summary>
    Convert,

    /// <summary>Operations that transform or inspect files without changing their nature.</summary>
    Tools,
}

/// <summary>An operation that can be applied to one or more dropped files.</summary>
public abstract class FileAction
{
    public abstract string Id { get; }

    /// <summary>Main label: a format name for conversions ("PNG"), a short verb phrase for tools.</summary>
    public abstract string Title { get; }

    /// <summary>Optional secondary label shown under conversion tiles ("Audio", "Pages").</summary>
    public virtual string? Subtitle => null;

    public abstract ActionGroup Group { get; }

    /// <summary>Segoe Fluent Icons / Segoe MDL2 Assets glyph used by tool rows.</summary>
    public virtual string Glyph => "\uE8A5";

    public virtual ExternalTool Requires => ExternalTool.None;

    public virtual int MinInputs => 1;

    /// <summary>Extension produced by a conversion, used to hide no-op conversions (PNG to PNG).</summary>
    public virtual string? TargetExtension => null;

    public abstract bool Accepts(string path);

    public virtual bool IsAvailableFor(IReadOnlyList<string> inputs)
    {
        if (inputs.Count < MinInputs || !inputs.All(Accepts))
        {
            return false;
        }

        return TargetExtension is null
            || inputs.Any(i => !Extensions.SameFormat(FileKinds.ExtensionOf(i), TargetExtension));
    }

    public abstract Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Runs the same operation on each input independently and collects per-file failures.</summary>
public abstract class PerFileAction : FileAction
{
    public override async Task<ActionResult> RunAsync(
        IReadOnlyList<string> inputs,
        ActionContext context,
        IProgress<ActionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var outputs = new List<string>();
        var errors = new List<ActionError>();

        for (var i = 0; i < inputs.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = i;
            var input = inputs[i];
            progress?.Report(new ActionProgress(index, inputs.Count, input, null));
            var fileProgress = progress is null
                ? null
                : new InlineProgress<double>(f => progress.Report(new ActionProgress(index, inputs.Count, input, f)));

            try
            {
                outputs.Add(await ProcessAsync(input, context, fileProgress, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (MissingToolException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(new ActionError(input, ex.Message));
            }
        }

        progress?.Report(new ActionProgress(inputs.Count, inputs.Count, null, null));
        return new ActionResult(outputs, errors);
    }

    protected abstract Task<string> ProcessAsync(
        string input,
        ActionContext context,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}

/// <summary>Synchronous progress that reports on the calling thread (avoids reordering of fine-grained updates).</summary>
internal sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}
