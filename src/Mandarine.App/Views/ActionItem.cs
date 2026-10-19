using System.ComponentModel;
using Mandarine.Core;
using Mandarine.Core.Actions;

namespace Mandarine.App.Views;

/// <summary>An action as shown in the panel, with its drag-over state.</summary>
public sealed class ActionItem(FileAction action, bool needsInstall) : INotifyPropertyChanged
{
    private bool _isHot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public FileAction Action { get; } = action;

    public string Title => Action.Title;

    public string? Subtitle => Action.Subtitle;

    public string Glyph => Action.Glyph;

    /// <summary>The action relies on FFmpeg or LibreOffice, which is not installed yet.</summary>
    public bool NeedsInstall { get; } = needsInstall;

    public string Description
    {
        get
        {
            var label = Action.Group == ActionGroup.Convert
                ? $"Convertir en {Action.Title}" + (Action.Subtitle is { } s ? $" ({s})" : string.Empty)
                : Action.Title;
            return NeedsInstall ? $"{label}. Nécessite {ToolLocator.DisplayName(Action.Requires)}." : label;
        }
    }

    /// <summary>UI Automation names the item containers after it (otherwise the type name).</summary>
    public override string ToString() => Description;

    public string WorkingLabel => Action.Group == ActionGroup.Convert ? $"Conversion en {Action.Title}" : Action.Title;

    public bool IsHot
    {
        get => _isHot;
        set
        {
            if (_isHot != value)
            {
                _isHot = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHot)));
            }
        }
    }
}
