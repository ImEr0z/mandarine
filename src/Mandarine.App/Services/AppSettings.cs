using System.Text.Json;
using System.Text.Json.Serialization;
using Mandarine.Core;
using Mandarine.Core.Actions;

namespace Mandarine.App.Services;

public enum TriggerMode
{
    /// <summary>Hold Shift while dragging (Tangerine's gesture). In Explorer, Shift also means "move".</summary>
    Shift,

    /// <summary>Hold Alt while dragging.</summary>
    Alt,

    /// <summary>Shake the file left and right while dragging, no modifier needed.</summary>
    Shake,

    Disabled,
}

public sealed class AppSettings
{
    public TriggerMode Trigger { get; set; } = TriggerMode.Shift;

    /// <summary>Only react to drags that start in File Explorer or on the desktop.</summary>
    public bool ExplorerOnly { get; set; } = true;

    /// <summary>Folder receiving converted files; null writes next to the original.</summary>
    public string? OutputDirectory { get; set; }

    public int Quality { get; set; } = 85;

    public bool LaunchAtStartup { get; set; } = true;

    public bool ExplorerContextMenu { get; set; } = true;

    public string? FfmpegPath { get; set; }

    public string? LibreOfficePath { get; set; }

    public bool Paused { get; set; }

    public bool FirstRunDone { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    public ToolLocator CreateToolLocator() => new(new Dictionary<ExternalTool, string?>
    {
        [ExternalTool.Ffmpeg] = FfmpegPath,
        [ExternalTool.LibreOffice] = LibreOfficePath,
    });
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mandarine", "settings.json"))
    {
    }

    public SettingsStore(string path)
    {
        FilePath = path;
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            }
        }
        catch (JsonException)
        {
            // Keep the unreadable file for inspection and start again from the defaults.
            File.Copy(FilePath, FilePath + ".bak", overwrite: true);
        }
        catch (IOException)
        {
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, overwrite: true);
    }
}
