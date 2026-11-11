using System.Text.Json;
using System.Text.Json.Serialization;
using Mandarine.Core;
using Mandarine.Core.Actions;

namespace Mandarine.App.Services;

public enum TriggerMode
{
    /// <summary>Hold Shift while dragging. In Explorer, Shift also means "move".</summary>
    Shift,

    /// <summary>Hold Alt while dragging.</summary>
    Alt,

    /// <summary>Hold Ctrl while dragging. In Explorer, Ctrl also means "copy", which is harmless.</summary>
    Ctrl,

    /// <summary>Tap Shift on its own with files selected in Explorer or on the desktop: no drag at all.</summary>
    ShiftTap,

    /// <summary>Shake the file left and right while dragging, no modifier needed.</summary>
    Shake,

    Disabled,
}

public enum PanelStyle
{
    Wheel,
    List,
}

/// <summary>What differs for one type of file; an empty or zero value falls back to the general setting.</summary>
public sealed class KindSettings
{
    /// <summary>Folder receiving the converted files of this type; empty uses the general one.</summary>
    public string? OutputDirectory { get; set; }

    /// <summary>Encoder quality (1-100); 0 uses the general one.</summary>
    public int Quality { get; set; }

    /// <summary>Longest side of converted images in pixels; 0 keeps the size.</summary>
    public int MaxDimension { get; set; }

    /// <summary>Resolution of pages rendered from a PDF; 0 means 150.</summary>
    public int Dpi { get; set; }

    /// <summary>Added to the name of created files: "photo" becomes "photo_web".</summary>
    public string? NameSuffix { get; set; }

    public KindSettings Clone() => (KindSettings)MemberwiseClone();
}

public sealed class AppSettings
{
    public KindSettings Images { get; set; } = new();

    public KindSettings Pdfs { get; set; } = new();

    public KindSettings Others { get; set; } = new();

    public KindSettings For(FileKind kind) => kind switch
    {
        FileKind.Image => Images,
        FileKind.Pdf => Pdfs,
        _ => Others,
    };

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

    /// <summary>Version whose "Nouveautés" window was already shown; it opens once per new version.</summary>
    public string? LastSeenNews { get; set; }

    /// <summary>Formats on a wheel or as a list.</summary>
    public PanelStyle Style { get; set; } = PanelStyle.Wheel;

    /// <summary>Soft sounds when moving over the wheel and when a conversion ends.</summary>
    public bool Sounds { get; set; } = true;

    public SoundTheme HoverSound { get; set; } = SoundTheme.Soft;

    public SoundTheme DoneSound { get; set; } = SoundTheme.Soft;

    /// <summary>Shortcut that opens the wheel for the files selected in Explorer or on the desktop (MOD_* flags).</summary>
    public uint HotkeyModifiers { get; set; } = Hotkey.Control | Hotkey.Shift;

    /// <summary>Virtual-key code of the shortcut; 0 disables it.</summary>
    public int HotkeyKey { get; set; } = 0x20; // Space

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.Images = Images.Clone();
        copy.Pdfs = Pdfs.Clone();
        copy.Others = Others.Clone();
        return copy;
    }

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
                foreach (var kind in new[] { settings.Images, settings.Pdfs, settings.Others })
                {
                    kind.Quality = Math.Clamp(kind.Quality, 0, 100);
                    kind.MaxDimension = Math.Clamp(kind.MaxDimension, 0, 20000);
                    kind.Dpi = Math.Clamp(kind.Dpi, 0, 600);
                }
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
