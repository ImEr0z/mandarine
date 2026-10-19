using System.Windows;
using System.Windows.Threading;
using Mandarine.App.Gestures;
using Mandarine.App.Interop;
using Mandarine.App.Services;
using Mandarine.App.Views;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Microsoft.Win32;

namespace Mandarine.App;

public partial class App : Application, IPanelHost
{
    private static readonly TimeSpan LaunchBatchWindow = TimeSpan.FromMilliseconds(600);

    private readonly SettingsStore _store = new();
    private readonly List<string> _queuedFiles = [];
    private AppSettings _settings = new();
    private ToolLocator _tools = new();
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private MouseHook? _hook;
    private DragGestureDetector? _detector;
    private ConverterPanel? _gesturePanel;
    private ConverterPanel? _dropZone;
    private SettingsWindow? _settingsWindow;
    private DispatcherTimer? _batchTimer;

    public ActionCatalog Catalog { get; } = new();

    public ToolLocator Tools => _tools;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _instance = new SingleInstance("Mandarine");
        if (!_instance.IsFirstInstance)
        {
            // Let the running instance bring its panel to the foreground.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            _instance.TrySendToFirstInstance(e.Args);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        _settings = _store.Load();
        _tools = _settings.CreateToolLocator();
        WarmUpTools();
        ThemeManager.Apply(this);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _tray = new TrayIcon(_settings.Paused);
        _tray.DropZoneRequested += ShowDropZone;
        _tray.SettingsRequested += OpenSettings;
        _tray.PauseChanged += SetPaused;
        _tray.ExitRequested += () => Shutdown();

        _detector = new DragGestureDetector();
        _detector.Triggered += point => Dispatcher.BeginInvoke(() => OnGestureTriggered(point));
        _detector.Released += () => Dispatcher.BeginInvoke(OnGestureReleased);
        _hook = new MouseHook();
        _hook.LeftButtonDown += _detector.OnButtonDown;
        _hook.Moved += _detector.OnMove;
        _hook.LeftButtonUp += _detector.OnButtonUp;
        ApplyGestureSettings();

        _instance.ArgumentsReceived += args => Dispatcher.BeginInvoke(() => HandleCommandLine(CommandLine.Parse(args)));
        _instance.Listen();

        var commandLine = CommandLine.Parse(e.Args);
        if (!_settings.FirstRunDone)
        {
            CompleteFirstRun();
            commandLine = commandLine with { Background = true };
        }

        HandleCommandLine(commandLine);
    }

    private void CompleteFirstRun()
    {
        try
        {
            ShellIntegration.Apply(_settings);
        }
        catch (Exception)
        {
            // Best effort (registry policies, COM "Send to" shortcut): the settings window lets the user retry.
        }

        _settings.FirstRunDone = true;
        _store.Save(_settings);
        _tray?.Notify("Mandarine est prêt",
            "Commencez à faire glisser un fichier depuis l'Explorateur, puis appuyez sur Maj : "
            + "les formats apparaissent sous le curseur. Clic droit sur l'icône pour les paramètres.");
    }

    private void HandleCommandLine(CommandLine commandLine)
    {
        if (commandLine.Files.Count > 0)
        {
            QueueFiles(commandLine.Files);
        }
        else if (commandLine.Settings)
        {
            OpenSettings();
        }
        else if (commandLine.DropZone)
        {
            ShowDropZone();
        }
        else if (!commandLine.Background)
        {
            // Plain launch (Start menu, double-click): no window, just confirm where Mandarine lives.
            _tray?.Notify("Mandarine est lancé",
                "Il attend dans la zone de notification. Clic sur l'icône pour la zone de dépôt.");
        }
    }

    /// <summary>
    /// Explorer launches one process per selected file from the context menu: collect the files
    /// forwarded within a short window and open a single panel for all of them.
    /// </summary>
    private void QueueFiles(IEnumerable<string> files)
    {
        _queuedFiles.AddRange(files.Where(f => !_queuedFiles.Contains(f, StringComparer.OrdinalIgnoreCase)));
        if (_batchTimer is null)
        {
            _batchTimer = new DispatcherTimer { Interval = LaunchBatchWindow };
            _batchTimer.Tick += (_, _) =>
            {
                _batchTimer.Stop();
                var batch = _queuedFiles.ToList();
                _queuedFiles.Clear();
                if (batch.Count > 0)
                {
                    new ConverterPanel(this, PanelMode.Picker).ShowPicker(batch, NativeMethods.CursorPosition());
                }
            };
        }

        _batchTimer.Stop();
        _batchTimer.Start();
    }

    // ---- Gesture ----

    private void OnGestureTriggered(NativeMethods.POINT cursor)
    {
        if (_settings.Paused)
        {
            return;
        }

        var panel = new ConverterPanel(this, PanelMode.Gesture);
        panel.Closed += (_, _) =>
        {
            if (ReferenceEquals(_gesturePanel, panel))
            {
                _gesturePanel = null;
            }
        };
        _gesturePanel = panel;
        panel.ShowArmed(cursor);
    }

    private void OnGestureReleased() => _gesturePanel?.OnGestureReleased();

    private void ApplyGestureSettings()
    {
        _detector?.Configure(_settings.Trigger, _settings.ExplorerOnly);
        if (_hook is null)
        {
            return;
        }

        try
        {
            if (_settings.Paused || _settings.Trigger == TriggerMode.Disabled)
            {
                _hook.Stop();
            }
            else
            {
                _hook.Start();
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _tray?.Notify("Geste indisponible", $"Windows a refusé la détection du glisser ({ex.Message}). "
                + "Le clic droit et la zone de dépôt restent disponibles.");
        }
    }

    private void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        _store.Save(_settings);
        ApplyGestureSettings();
    }

    // ---- Windows ----

    private void ShowDropZone()
    {
        if (_dropZone is not null)
        {
            _dropZone.Activate();
            return;
        }

        _dropZone = new ConverterPanel(this, PanelMode.DropZone);
        _dropZone.Closed += (_, _) => _dropZone = null;
        _dropZone.ShowDropZone();
    }

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, InstallToolAsync);
        try
        {
            if (_settingsWindow.ShowDialog() == true && _settingsWindow.Result is { } updated)
            {
                ApplySettings(updated);
            }
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ApplySettings(AppSettings updated)
    {
        var previous = _settings;
        _settings = updated;
        _store.Save(_settings);
        _tools = _settings.CreateToolLocator();
        WarmUpTools();
        ApplyGestureSettings();

        try
        {
            if (previous.LaunchAtStartup != updated.LaunchAtStartup)
            {
                ShellIntegration.SetLaunchAtStartup(updated.LaunchAtStartup);
            }

            if (previous.ExplorerContextMenu != updated.ExplorerContextMenu)
            {
                ShellIntegration.SetExplorerIntegration(updated.ExplorerContextMenu);
            }
        }
        catch (Exception ex)
        {
            _tray?.Notify("Intégration Windows", $"Modification impossible : {ex.Message}");
        }
    }

    // ---- IPanelHost ----

    public ActionContext CreateContext() => new()
    {
        OutputDirectory = _settings.OutputDirectory,
        Quality = _settings.Quality,
        Tools = _tools,
    };

    public async Task<bool> InstallToolAsync(ExternalTool tool)
    {
        await ToolInstaller.InstallAsync(tool);
        _tools.Refresh();
        return _tools.IsAvailable(tool);
    }

    /// <summary>Looks tools up in the background so the first drag-over never waits on disk scans.</summary>
    private void WarmUpTools()
    {
        var tools = _tools;
        Task.Run(() =>
        {
            tools.Find(ExternalTool.Ffmpeg);
            tools.Find(ExternalTool.LibreOffice);
        });
    }

    // ---- Lifetime ----

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General)
        {
            Dispatcher.BeginInvoke(() => ThemeManager.Apply(this));
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background utility must survive a bug in one conversion: log, notify, keep running.
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarine");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"[{DateTime.Now:O}] {e.Exception}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }

        _tray?.Notify("Mandarine a rencontré une erreur", e.Exception.Message);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _hook?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
