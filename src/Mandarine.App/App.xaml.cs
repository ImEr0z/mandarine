using System.Runtime;
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
    private static readonly TimeSpan IdleReliefDelay = TimeSpan.FromSeconds(3);

    private readonly SettingsStore _store = new();
    private readonly List<string> _queuedFiles = [];
    private AppSettings _settings = new();
    private ToolLocator _tools = new();
    private SingleInstance? _instance;
    private TrayIcon? _tray;
    private MouseHook? _hook;
    private DragGestureDetector? _detector;
    private ConverterPanel? _gesturePanel;
    private SettingsWindow? _settingsWindow;
    private Hotkey? _hotkey;
    private DispatcherTimer? _batchTimer;
    private DispatcherTimer? _reliefTimer;

    public ActionCatalog Catalog { get; } = Mandarine.App.Actions.WindowsActions.Catalog();

    public ToolLocator Tools => _tools;

    public bool UseWheel => _settings.Style == PanelStyle.Wheel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Explorer starts the context menu verb in the folder of the clicked file (often Downloads):
        // never load a DLL or run a program from there. Relative arguments are resolved first.
        NativeMethods.SetDefaultDllDirectories(NativeMethods.LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        NativeMethods.SetDllDirectory(string.Empty);
        var arguments = e.Args.Select(a => a.StartsWith("--", StringComparison.Ordinal) ? a : Path.GetFullPath(a)).ToArray();
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        if (CommandLine.Parse(arguments).Worker)
        {
            // A conversion started by the tray app: no window, no hook, no tray, no single-instance check.
            Shutdown(ConversionWorker.Run());
            return;
        }

        _instance = new SingleInstance("Mandarine");
        if (!_instance.IsFirstInstance)
        {
            // Let the running instance bring its panel to the foreground.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            if (!_instance.TrySendToFirstInstance(arguments))
            {
                MessageBox.Show("Mandarine est déjà lancé mais ne répond pas. Quittez-le depuis la zone de notification, puis réessayez.",
                    "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ErrorLog.Write(args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ErrorLog.Write(args.Exception);
            args.SetObserved();
        };
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _settings = _store.Load();
        UiSounds.Enabled = _settings.Sounds;
        _tools = _settings.CreateToolLocator();
        WarmUpTools();
        ThemeManager.Apply(this);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

        _tray = new TrayIcon(_settings.Paused);
        _tray.SettingsRequested += OpenSettings;
        _tray.PauseChanged += SetPaused;
        _tray.ExitRequested += () => Shutdown();
        _tray.UninstallRequested += Uninstall;

        _detector = new DragGestureDetector();
        _detector.Triggered += point => Dispatcher.BeginInvoke(() => OnGestureTriggered(point));
        _detector.Released += () => Dispatcher.BeginInvoke(OnGestureReleased);
        _hook = new MouseHook();
        _hook.LeftButtonDown += _detector.OnButtonDown;
        _hook.Moved += _detector.OnMove;
        _hook.LeftButtonUp += _detector.OnButtonUp;
        ApplyGestureSettings();

        _hotkey = new Hotkey();
        _hotkey.Pressed += OnHotkeyPressed;
        ApplyHotkey(notifyOnFailure: true);

        _instance.ArgumentsReceived += args => Dispatcher.BeginInvoke(() => HandleCommandLine(CommandLine.Parse(args)));
        _instance.Listen();

        var commandLine = CommandLine.Parse(arguments);
        if (!_settings.FirstRunDone)
        {
            CompleteFirstRun();
            commandLine = commandLine with { Background = true };
        }

        try
        {
            ShellIntegration.RefreshPaths(_settings);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        HandleCommandLine(commandLine);
        ScheduleIdleRelief();
        _ = AppUpdater.CheckAsync();
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
        else if (!commandLine.Background)
        {
            // Plain launch (Start menu, double-click): no window, just confirm where Mandarine lives.
            _tray?.Notify("Mandarine est lancé",
                "Il attend dans la zone de notification : faites glisser un fichier puis appuyez sur Maj, ou utilisez le clic droit.");
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
                    Track(new ConverterPanel(this, PanelMode.Picker)).ShowPicker(batch, NativeMethods.CursorPosition());
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

        var panel = Track(new ConverterPanel(this, PanelMode.Gesture));
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
                + "Le clic droit reste disponible.");
        }
    }

    private void SetPaused(bool paused)
    {
        _settings.Paused = paused;
        _store.Save(_settings);
        ApplyGestureSettings();
    }

    // ---- Windows ----

    public void OpenSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = Track(new SettingsWindow(_settings, InstallToolAsync));
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
        UiSounds.Enabled = _settings.Sounds;
        _tools = _settings.CreateToolLocator();
        WarmUpTools();
        ApplyGestureSettings();
        ApplyHotkey(notifyOnFailure: true);

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
        if (Catalog.All.All(a => a.Requires == ExternalTool.None))
        {
            return; // this version uses no external tool: no disk scan at startup
        }

        var tools = _tools;
        Task.Run(() =>
        {
            tools.Find(ExternalTool.Ffmpeg);
            tools.Find(ExternalTool.LibreOffice);
        });
    }

    // ---- Idle footprint ----

    /// <summary>
    /// Showing a panel loads WPF rendering, fonts and the conversion libraries (tens of MB). Once the
    /// last window is gone, Mandarine goes back to waiting: collect everything and return the pages
    /// to Windows so that the idle footprint stays small.
    /// </summary>
    private T Track<T>(T window)
        where T : Window
    {
        // Full speed while a window is up (conversions), efficiency mode once Mandarine is waiting again.
        NativeMethods.SetEfficiencyMode(false);
        window.Closed += (_, _) => ScheduleIdleRelief();
        return window;
    }

    private void ScheduleIdleRelief()
    {
        if (_reliefTimer is null)
        {
            _reliefTimer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = IdleReliefDelay };
            _reliefTimer.Tick += (_, _) =>
            {
                _reliefTimer.Stop();
                if (Windows.Cast<Window>().Any(w => w.IsVisible))
                {
                    return;
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), -1, -1);
                NativeMethods.SetEfficiencyMode(true);
            };
        }

        _reliefTimer.Stop();
        _reliefTimer.Start();
    }

    // ---- Lifetime ----

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color)
        {
            Dispatcher.BeginInvoke(() => ThemeManager.Apply(this));
        }
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // A background utility must survive a bug in one conversion: log, notify, keep running.
        ErrorLog.Write(e.Exception);
        _tray?.Notify("Mandarine a rencontré une erreur", e.Exception.Message);
        e.Handled = true;
    }

    /// <summary>
    /// Windows silently drops a low-level hook that once answered too slowly (e.g. pages swapped out
    /// during sleep). Reinstall it whenever the session comes back so the gesture keeps working.
    /// </summary>
    private void RestartHook()
    {
        if (_hook is null || _settings.Paused || _settings.Trigger == TriggerMode.Disabled)
        {
            return;
        }

        _hook.Stop();
        ApplyGestureSettings();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect
            or SessionSwitchReason.RemoteConnect)
        {
            Dispatcher.BeginInvoke(RestartHook);
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Dispatcher.BeginInvoke(RestartHook);
        }
    }

    // ---- Shortcut ----

    private void ApplyHotkey(bool notifyOnFailure)
    {
        if (_hotkey is null)
        {
            return;
        }

        if (!_hotkey.Set(_settings.HotkeyModifiers, _settings.HotkeyKey) && notifyOnFailure)
        {
            _tray?.Notify("Raccourci indisponible",
                $"{Hotkey.Describe(_settings.HotkeyModifiers, _settings.HotkeyKey)} est déjà utilisé par une autre application. "
                + "Choisissez-en un autre dans les paramètres.");
        }
    }

    /// <summary>Selected files in Explorer or on the desktop + the shortcut: the wheel opens under the mouse.</summary>
    private void OnHotkeyPressed()
    {
        if (_settings.Paused)
        {
            return;
        }

        var files = ExplorerSelection.FromForegroundWindow();
        if (files.Count == 0)
        {
            _tray?.Notify("Aucun fichier sélectionné",
                "Sélectionnez un ou plusieurs fichiers dans l'Explorateur ou sur le Bureau, puis appuyez sur "
                + Hotkey.Describe(_settings.HotkeyModifiers, _settings.HotkeyKey) + ".");
            return;
        }

        Track(new ConverterPanel(this, PanelMode.Picker)).ShowPicker(files, NativeMethods.CursorPosition());
    }

    // ---- Uninstall ----

    private void Uninstall()
    {
        var answer = MessageBox.Show(
            "Retirer Mandarine de Windows ?\n\nLe clic droit, « Envoyer vers », le lancement au démarrage, le raccourci du menu Démarrer "
            + "et les paramètres seront supprimés. "
            + (AppUpdater.IsInstalled
                ? "L'application elle-même se retire depuis les Paramètres Windows, « Applications installées »."
                : "Il ne restera que Mandarine.exe, à supprimer vous-même."),
            "Désinstaller Mandarine", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            ShellIntegration.RemoveAll();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }

        _hook?.Dispose();
        _hook = null;
        foreach (var folder in new[]
                 {
                     Path.GetDirectoryName(_store.FilePath)!,
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mandarine"),
                     Path.Combine(Path.GetTempPath(), "Mandarine"),
                 })
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        // Do not write settings back on exit: there is nothing left to save to.
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _hook?.Dispose();
        _hotkey?.Dispose();
        _tray?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
