using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;
using Mandarine.App.Services;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Identical files of a folder, grouped. In each group you pick the one to keep; the others go to the Recycle Bin.</summary>
internal sealed class DuplicatesWindow : Window
{
    private readonly string _folder;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status = DialogStyle.Muted("Analyse…");
    private readonly CheckBox _recursive = new() { IsChecked = true };
    private readonly Button _apply;
    private readonly Dictionary<DuplicateGroup, string> _kept = [];
    private IReadOnlyList<DuplicateGroup> _groups = [];
    private int _generation;

    public DuplicatesWindow(string folder)
    {
        _folder = folder;
        Title = $"Doublons – {System.IO.Path.GetFileName(folder)}";
        DialogStyle.Frameless(this, 720);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _recursive.Content = new TextBlock { Text = "Inclure les sous-dossiers", FontSize = 13.5 };
        _recursive.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
        _recursive.Click += async (_, _) => await ScanAsync();
        _status.Margin = new Thickness(0, 10, 0, 8);
        var list = new ScrollViewer { Content = _rows, MaxHeight = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 6, 12, 6), Child = list };
        frame.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        frame.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        var close = DialogStyle.Button("Fermer", primary: false, isCancel: true);
        _apply = DialogStyle.Button("Mettre les copies à la corbeille", primary: true);
        _apply.IsEnabled = false;
        close.Click += (_, _) => Close();
        _apply.Click += (_, _) => Delete();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { close, _apply } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children = { DialogStyle.Header("", folder, "Trouver les doublons", Close), _recursive, _status, frame, buttons },
        });
        Content = card;
        DialogStyle.Appear(this, card);
        Loaded += async (_, _) => await ScanAsync();
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} Go",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} Mo",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0.#} Ko",
        _ => $"{bytes} o",
    };

    private async Task ScanAsync()
    {
        var generation = ++_generation;
        _apply.IsEnabled = false;
        _rows.Children.Clear();
        _status.Text = "Analyse du dossier…";
        var progress = new Progress<string>(name => { if (generation == _generation) { _status.Text = "Analyse : " + name; } });
        try
        {
            var recursive = _recursive.IsChecked == true;
            var groups = await Task.Run(() => DuplicateFinder.Find(_folder, recursive, progress));
            if (generation != _generation)
            {
                return;
            }

            _groups = groups;
            ShowGroups();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            _status.Text = "Analyse impossible : " + ex.Message;
        }
    }

    private void ShowGroups()
    {
        _kept.Clear();
        _rows.Children.Clear();
        foreach (var group in _groups)
        {
            _kept[group] = group.Files[0]; // the oldest one stays by default
            var title = new TextBlock { Text = $"{System.IO.Path.GetFileName(group.Files[0])} · {group.Files.Count} copies · {Size(group.Size)} chacune", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4), TextTrimming = TextTrimming.CharacterEllipsis };
            _rows.Children.Add(title);
            foreach (var file in group.Files)
            {
                var radio = new RadioButton { GroupName = group.Files[0], IsChecked = file == group.Files[0], Margin = new Thickness(0, 2, 0, 2) };
                radio.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
                radio.Content = new TextBlock
                {
                    Text = $"{System.IO.Path.GetRelativePath(_folder, file)}   ·   {File.GetLastWriteTime(file):d MMM yyyy}",
                    FontSize = 13.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = file,
                };
                var captured = group;
                var path = file;
                radio.Checked += (_, _) =>
                {
                    _kept[captured] = path;
                    UpdateSummary();
                };
                _rows.Children.Add(radio);
            }
        }

        _status.Text = _groups.Count == 0 ? "Aucun doublon trouvé." : string.Empty;
        UpdateSummary();
    }

    private IEnumerable<string> ToDelete() => _groups.SelectMany(g => g.Files.Where(f => f != _kept[g]));

    private void UpdateSummary()
    {
        var delete = ToDelete().ToList();
        var freed = _groups.Sum(g => g.Size * (g.Files.Count - 1));
        if (_groups.Count > 0)
        {
            _status.Text = $"{_groups.Count} groupe{(_groups.Count > 1 ? "s" : string.Empty)} de doublons. Cochez le fichier à garder dans chacun : "
                + $"{delete.Count} copie{(delete.Count > 1 ? "s" : string.Empty)} ({Size(freed)}) iront à la corbeille.";
        }

        _apply.IsEnabled = delete.Count > 0;
    }

    private void Delete()
    {
        var delete = ToDelete().ToList();
        if (MessageBox.Show(this, $"Mettre {delete.Count} fichier{(delete.Count > 1 ? "s" : string.Empty)} à la corbeille ?", "Mandarine", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var removed = 0;
        long freed = 0;
        foreach (var file in delete)
        {
            try
            {
                var size = new FileInfo(file).Length;
                FileSystem.DeleteFile(file, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                removed++;
                freed += size;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                ErrorLog.Write(ex);
            }
        }

        TrayBridge.Notify("Doublons supprimés", $"{removed} fichier{(removed > 1 ? "s" : string.Empty)} à la corbeille, {Size(freed)} libérés", _folder);
        Close();
    }
}
