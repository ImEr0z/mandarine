using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mandarine.App.Services;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Microsoft.Win32;

namespace Mandarine.App.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Func<ExternalTool, Task<bool>> _installTool;
    private uint _hotkeyModifiers;
    private int _hotkeyKey;

    internal SettingsWindow(AppSettings settings, Func<ExternalTool, Task<bool>> installTool)
    {
        InitializeComponent();
        _settings = settings.Clone();
        _installTool = installTool;

        TriggerBox.SelectedItem = TriggerBox.Items.Cast<ComboBoxItem>()
            .First(i => (string)i.Tag == _settings.Trigger.ToString());
        ExplorerOnlyBox.IsChecked = _settings.ExplorerOnly;
        NextToSourceRadio.IsChecked = string.IsNullOrWhiteSpace(_settings.OutputDirectory);
        FolderRadio.IsChecked = !NextToSourceRadio.IsChecked;
        FolderBox.Text = _settings.OutputDirectory ?? string.Empty;
        QualitySlider.Value = _settings.Quality;
        StartupBox.IsChecked = _settings.LaunchAtStartup;
        ContextMenuBox.IsChecked = _settings.ExplorerContextMenu;
        WheelRadio.IsChecked = _settings.Style == PanelStyle.Wheel;
        ListRadio.IsChecked = !WheelRadio.IsChecked;
        SoundsBox.IsChecked = _settings.Sounds;
        _hotkeyModifiers = _settings.HotkeyModifiers;
        _hotkeyKey = _settings.HotkeyKey;
        HotkeyBox.Text = Hotkey.Describe(_hotkeyModifiers, _hotkeyKey);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Mandarine {version?.ToString(3)} · inspiré de Tangerine pour Mac";
        RefreshTools();
    }

    /// <summary>The validated settings, set when the user saves.</summary>
    internal AppSettings? Result { get; private set; }

    private void OnTriggerChanged(object sender, SelectionChangedEventArgs e)
    {
        TriggerHint.Text = SelectedTrigger switch
        {
            TriggerMode.Shift => "Comme Tangerine. Dans l'Explorateur, Maj+glisser sert aussi à déplacer des fichiers : "
                + "si vous le faites souvent, préférez « secouer ».",
            TriggerMode.Alt => "Alt+glisser sert aussi à créer des raccourcis dans l'Explorateur.",
            TriggerMode.Ctrl => "Ctrl+glisser sert aussi à copier dans l'Explorateur : sans risque, l'original ne bouge jamais.",
            TriggerMode.Shake => "Aucune touche à retenir : trois allers-retours rapides pendant le glissement.",
            _ => "Le menu reste accessible par clic droit, « Envoyer vers » et l'icône de notification.",
        };
    }

    private TriggerMode SelectedTrigger =>
        Enum.Parse<TriggerMode>((string)((ComboBoxItem)TriggerBox.SelectedItem).Tag);

    // ---- Shortcut recorder (keys are read only while this box has focus) ----

    private void OnHotkeyFocus(object sender, RoutedEventArgs e) => HotkeyBox.Text = "Appuyez sur la combinaison…";

    private void OnHotkeyBlur(object sender, RoutedEventArgs e) => HotkeyBox.Text = Hotkey.Describe(_hotkeyModifiers, _hotkeyKey);

    private void OnHotkeyClear(object sender, RoutedEventArgs e)
    {
        _hotkeyModifiers = 0;
        _hotkeyKey = 0;
        HotkeyBox.Text = Hotkey.Describe(0, 0);
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab && Keyboard.Modifiers == ModifierKeys.None)
        {
            return; // keep keyboard navigation out of the box
        }

        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Escape)
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
            HotkeyBox.Text = "Ajoutez Ctrl, Alt ou Win (ou une touche F1 à F24)";
            return;
        }

        _hotkeyModifiers = modifiers;
        _hotkeyKey = KeyInterop.VirtualKeyFromKey(key);
        HotkeyBox.Text = Hotkey.Describe(_hotkeyModifiers, _hotkeyKey);
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

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        if (FolderRadio.IsChecked == true && !Path.IsPathFullyQualified(folder))
        {
            MessageBox.Show(this, "Choisissez un dossier de destination valide.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.Trigger = SelectedTrigger;
        _settings.ExplorerOnly = ExplorerOnlyBox.IsChecked == true;
        _settings.OutputDirectory = FolderRadio.IsChecked == true ? folder : null;
        _settings.Quality = (int)QualitySlider.Value;
        _settings.LaunchAtStartup = StartupBox.IsChecked == true;
        _settings.ExplorerContextMenu = ContextMenuBox.IsChecked == true;
        _settings.Style = WheelRadio.IsChecked == true ? PanelStyle.Wheel : PanelStyle.List;
        _settings.Sounds = SoundsBox.IsChecked == true;
        _settings.HotkeyModifiers = _hotkeyModifiers;
        _settings.HotkeyKey = _hotkeyKey;
        Result = _settings;
        DialogResult = true;
    }
}
