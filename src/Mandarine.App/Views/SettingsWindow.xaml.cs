using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mandarine.App.Services;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Microsoft.Win32;

namespace Mandarine.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<ExternalTool, Task<bool>> _installTool;
    private readonly Action<AppSettings> _applied;
    private readonly System.Windows.Threading.DispatcherTimer _saveTimer;
    private bool _loading = true;
    private uint _hotkeyModifiers;
    private int _hotkeyKey;

    internal SettingsWindow(AppSettings settings, Func<ExternalTool, Task<bool>> installTool, Action<AppSettings> applied)
    {
        InitializeComponent();
        _settings = settings.Clone();
        _installTool = installTool;
        _applied = applied;
        _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _saveTimer.Tick += (_, _) => Commit();

        TriggerBox.SelectedItem = TriggerBox.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag == _settings.Trigger.ToString());
        ExplorerOnlyBox.IsChecked = _settings.ExplorerOnly;
        NextToSourceRadio.IsChecked = string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        FolderRadio.IsChecked = !NextToSourceRadio.IsChecked;
        FolderBox.Text = _settings.OutputDirectory ?? string.Empty;
        QualitySlider.Value = _settings.Quality;
        StartupBox.IsChecked = _settings.LaunchAtStartup;
        StartMinimizedBox.IsChecked = _settings.StartMinimized;
        ContextMenuBox.IsChecked = _settings.ExplorerContextMenu;
        WheelRadio.IsChecked = _settings.Style == PanelStyle.Wheel;
        ListRadio.IsChecked = !WheelRadio.IsChecked;
        SoundsBox.IsChecked = _settings.Sounds;
        SelectSound(HoverSoundBox, _settings.HoverSound);
        SelectSound(DoneSoundBox, _settings.DoneSound);
        _hotkeyModifiers = _settings.HotkeyModifiers;
        _hotkeyKey = _settings.HotkeyKey;
        ShowHotkey();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Mandarine, version {version?.ToString(3)}";
        RefreshTools();
        _loading = false;
    }

    private void OnTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        QueueSave();
        TriggerHint.Text = SelectedTrigger switch
        {
            TriggerMode.Shift => "Dans l'Explorateur, Maj+glisser sert aussi à déplacer des fichiers : "
                + "si vous le faites souvent, préférez « Secouer ».",
            TriggerMode.Alt => "Alt+glisser sert aussi à créer des raccourcis dans l'Explorateur.",
            TriggerMode.Ctrl => "Ctrl+glisser sert aussi à copier dans l'Explorateur : sans risque, l'original ne bouge jamais.",
            TriggerMode.ShiftTap => "Sélectionnez des fichiers dans l'Explorateur ou sur le Bureau, puis tapez sur Maj, seule. "
                + "Marche aussi en glissant un fichier : appuyez sur Maj pendant le glissement.",
            TriggerMode.Shake => "Trois allers-retours rapides pendant le glissement, sans touche à retenir.",
            _ => "Le menu reste accessible par clic droit, « Envoyer vers » et l'icône de notification.",
        };
        ExplorerOnlyRow.IsEnabled = SelectedTrigger != TriggerMode.Disabled;
    }

    private TriggerMode SelectedTrigger =>
        Enum.Parse<TriggerMode>((string)((ComboBoxItem)TriggerBox.SelectedItem).Tag);

    // ---- Shortcut recorder (keys are read only while this box has focus) ----

    private bool _recording;

    private void OnHotkeyRecord(object sender, RoutedEventArgs e)
    {
        _recording = true;
        HotkeyRecord.Content = "Appuyez…";
        ShowHotkey();
    }

    private void OnHotkeyBlur(object sender, RoutedEventArgs e) => StopRecording();

    private void StopRecording()
    {
        _recording = false;
        HotkeyRecord.Content = "Modifier";
        ShowHotkey();
    }

    private void OnHotkeyClear(object sender, RoutedEventArgs e)
    {
        _hotkeyModifiers = 0;
        _hotkeyKey = 0;
        StopRecording();
        QueueSave();
    }

    /// <summary>Shows the shortcut as one key cap per key, or a hint while recording.</summary>
    private void ShowHotkey(string? message = null)
    {
        HotkeyChips.Children.Clear();
        var parts = message is not null ? new[] { message }
            : _recording ? new[] { "Appuyez sur les touches…" }
            : _hotkeyKey == 0 ? new[] { "Aucun" }
            : Hotkey.Describe(_hotkeyModifiers, _hotkeyKey).Split(" + ");
        var isKeys = message is null && !_recording && _hotkeyKey != 0;
        foreach (var part in parts)
        {
            var text = new TextBlock { Text = part, FontWeight = isKeys ? FontWeights.SemiBold : FontWeights.Normal };
            if (!isKeys)
            {
                text.Opacity = 0.72;
                HotkeyChips.Children.Add(text);
                continue;
            }

            HotkeyChips.Children.Add(new Border
            {
                Child = text,
                Background = (Brush)FindResource("ChipFill"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 4, 0),
            });
        }
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            return; // keep keyboard navigation working
        }

        e.Handled = true;
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin)
        {
            return;
        }

        uint modifiers = 0;
        var held = Keyboard.Modifiers;
        if (held.HasFlag(ModifierKeys.Control)) modifiers |= Hotkey.Control;
        if (held.HasFlag(ModifierKeys.Alt)) modifiers |= Hotkey.Alt;
        if (held.HasFlag(ModifierKeys.Shift)) modifiers |= Hotkey.Shift;
        if (held.HasFlag(ModifierKeys.Windows)) modifiers |= Hotkey.Win;

        // A plain letter (or Shift+letter) would be stolen from every text field in Windows.
        var isFunctionKey = key is >= Key.F1 and <= Key.F24;
        if ((modifiers & (Hotkey.Control | Hotkey.Alt | Hotkey.Win)) == 0 && !isFunctionKey)
        {
            ShowHotkey("Ajoutez Ctrl, Alt ou Win (ou F1 à F24)");
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyKey = KeyInterop.VirtualKeyFromKey(key);
        StopRecording();
        QueueSave();
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier des fichiers créés" };
        if (Directory.Exists(FolderBox.Text))
        {
            dialog.InitialDirectory = FolderBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FolderBox.Text = dialog.FolderName;
            FolderRadio.IsChecked = true;
            QueueSave();
        }
    }

    private void OnPickTool(object sender, RoutedEventArgs e)
    {
        var tool = Enum.Parse<ExternalTool>((string)((Button)sender).Tag);
        var executable = tool == ExternalTool.Ffmpeg ? "ffmpeg.exe" : "soffice.exe";
        var dialog = new OpenFileDialog
        {
            Title = $"Emplacement de {executable}",
            Filter = $"{executable}|{executable}",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (tool == ExternalTool.Ffmpeg)
        {
            _settings.FfmpegPath = dialog.FileName;
        }
        else
        {
            _settings.LibreOfficePath = dialog.FileName;
        }

        RefreshTools();
        QueueSave();
    }

    private async void OnInstallTool(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var tool = Enum.Parse<ExternalTool>((string)button.Tag);
        button.IsEnabled = false;
        button.Content = "Installation…";
        try
        {
            await _installTool(tool);
        }
        finally
        {
            button.Content = "Installer";
            RefreshTools();
        }
    }

    private void RefreshTools()
    {
        var locator = _settings.CreateToolLocator();
        Show(ExternalTool.Ffmpeg, FfmpegStatus, FfmpegInstall);
        Show(ExternalTool.LibreOffice, LibreOfficeStatus, LibreOfficeInstall);

        void Show(ExternalTool tool, TextBlock status, Button install)
        {
            var path = locator.Find(tool);
            status.Text = path is null ? "Non installé" : $"✓ {path}";
            status.ToolTip = path;
            install.IsEnabled = path is null;
        }
    }

    // ---- Automatic saving: every change is applied after a short pause ----

    private void OnChanged(object sender, RoutedEventArgs e) => QueueSave();

    private void OnQualityChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => QueueSave();

    private void OnFolderTextChanged(object sender, TextChangedEventArgs e) => QueueSave();

    private void OnHoverSoundChanged(object sender, SelectionChangedEventArgs e) => SoundPicked(HoverSoundBox, hover: true);

    private void OnDoneSoundChanged(object sender, SelectionChangedEventArgs e) => SoundPicked(DoneSoundBox, hover: false);

    private void OnPlayHover(object sender, RoutedEventArgs e) => UiSounds.Preview(PickedSound(HoverSoundBox), hover: true);

    private void OnPlayDone(object sender, RoutedEventArgs e) => UiSounds.Preview(PickedSound(DoneSoundBox), hover: false);

    private void SoundPicked(ComboBox box, bool hover)
    {
        if (_loading)
        {
            return;
        }

        UiSounds.Preview(PickedSound(box), hover);
        QueueSave();
    }

    private static SoundTheme PickedSound(ComboBox box) => Enum.Parse<SoundTheme>((string)((ComboBoxItem)box.SelectedItem).Tag);

    private static void SelectSound(ComboBox box, SoundTheme theme) =>
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == theme.ToString());

    private void QueueSave()
    {
        if (_loading)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_saveTimer.IsEnabled)
        {
            Commit(); // flush a change made less than a moment ago
        }
    }

    private void Commit()
    {
        _saveTimer.Stop();
        var folder = FolderBox.Text.Trim();
        if (FolderRadio.IsChecked != true)
        {
            _settings.OutputDirectory = null;
        }
        else if (Path.IsPathFullyQualified(folder))
        {
            _settings.OutputDirectory = folder;
        } // else: keep the last valid folder while the path is still being typed

        _settings.Trigger = SelectedTrigger;
        _settings.ExplorerOnly = ExplorerOnlyBox.IsChecked == true;
        _settings.Quality = (int)QualitySlider.Value;
        _settings.LaunchAtStartup = StartupBox.IsChecked == true;
        _settings.StartMinimized = StartMinimizedBox.IsChecked == true;
        _settings.ExplorerContextMenu = ContextMenuBox.IsChecked == true;
        _settings.Style = WheelRadio.IsChecked == true ? PanelStyle.Wheel : PanelStyle.List;
        _settings.Sounds = SoundsBox.IsChecked == true;
        _settings.HoverSound = PickedSound(HoverSoundBox);
        _settings.DoneSound = PickedSound(DoneSoundBox);
        _settings.HotkeyModifiers = _hotkeyModifiers;
        _settings.HotkeyKey = _hotkeyKey;
        _applied(_settings.Clone());
    }
}
