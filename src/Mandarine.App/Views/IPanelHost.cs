using Mandarine.Core;
using Mandarine.Core.Actions;

namespace Mandarine.App.Views;

/// <summary>Services the application provides to its panels.</summary>
internal interface IPanelHost
{
    ActionCatalog Catalog { get; }

    ToolLocator Tools { get; }

    /// <summary>Show actions on a wheel (Tangerine style) rather than as a list.</summary>
    bool UseWheel { get; }

    ActionContext CreateContext();

    /// <summary>Installs a missing tool; true when it can be used afterwards.</summary>
    Task<bool> InstallToolAsync(ExternalTool tool);

    void OpenSettings();
}
