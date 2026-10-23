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

    /// <summary>Hold Ctrl while dragging. In Explorer, Ctrl also means "copy", which is harmless.</summary>
    Ctrl,

    /// <summary>Shake the file left and right while dragging, no modifier needed.</summary>
    Shake,

    Disabled,
}

public enum PanelStyle
{
    Wheel,
    List,
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

    /// <summary>At login, start quietly in the notification area (no "Mandarine est lancé" message).</summary>
    public bool StartMinimized { get; set; } = true;

    public bool ExplorerContextMenu { get; set; } = true;

    public string? FfmpegPath { get; set; }

    public string? LibreOfficePath { get; set; }

    public bool Paused { get; set; }

    public bool FirstRunDone { get; set; }

    /// <summary>Formats on a wheel (like Tangerine) or as a list.</summary>
    public PanelStyle Style { get; set; } = PanelStyle.Wheel;

    /// <summary>Soft sounds when moving over the wheel and when a conversion ends.</summary>
    public bool Sounds { get; set; } = true;

    /// <summary>Shortcut that opens the wheel for the files selected in Explorer or on the desktop (MOD_* flags).</summary>
    public uint HotkeyModifiers { get; set; } = Hotkey.Control | Hotkey.Shift;

    /// <summary>Virtual-key code of the shortcut; 0 disables it.</summary>
    public int HotkeyKey { get; set; } = 0x20; // Space

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
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                settings.Quality = Math.Clamp(settings.Quality, 1, 100);
                if (!Enum.IsDefined(settings.Trigger))
                {
                    settings.Trigger = TriggerMode.Shift;
                }

                return settings;
            }
        }
        catch (JsonException)
        {
            // Keep the unreadable file for inspection and start again from the defaults.
            try
            {
                File.Copy(FilePath, FilePath + ".bak", overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, Options);
            stream.Flush(flushToDisk: true); // survive a power cut between the write and the rename
        }

        File.Move(temporary, FilePath, overwrite: true);
    }
}
