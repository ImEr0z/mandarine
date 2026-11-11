using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mandarine.App.Services;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Sorts the files of a folder into sub-folders by kind, year or month, shows what will move, and can undo.</summary>
internal sealed class FolderSortWindow : Window
{
    private readonly string _folder;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary = DialogStyle.Muted(string.Empty);
    private readonly List<(Border Pill, SortMode Mode)> _modes = [];
    private readonly Button _apply;
    private readonly Button _cancel;
    private readonly Button _undo;
    private SortMode _mode = SortMode.Type;
    private IReadOnlyList<SortMove> _plan = [];
    private IReadOnlyList<SortMove> _done = [];

    public FolderSortWindow(string folder)
    {
        _folder = folder;
        Title = $"Trier – {System.IO.Path.GetFileName(folder)}";
        DialogStyle.Frameless(this, 600);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var choice = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var (label, mode) in new[] { ("Par type", SortMode.Type), ("Par année", SortMode.Year), ("Par mois", SortMode.Month) })
        {
            var pill = new Border { CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(1), Child = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold } };
            pill.SetResourceReference(Border.BorderBrushProperty, "Accent");
            pill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _mode = mode;
                Refresh();
            };
            _modes.Add((pill, mode));
            choice.Children.Add(pill);
        }

        _summary.Margin = new Thickness(0, 4, 0, 8);
        var list = new ScrollViewer { Content = _rows, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var frame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), Child = list };
        frame.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        frame.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        _cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        _apply = DialogStyle.Button("Trier", primary: true, isDefault: true);
        _undo = DialogStyle.Button("Annuler le tri", primary: false);
        _undo.Visibility = Visibility.Collapsed;
        _cancel.Click += (_, _) => Close();
        _apply.Click += (_, _) => Apply();
        _undo.Click += (_, _) => Undo();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { _undo, _cancel, _apply } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children = { DialogStyle.Header("", folder, "Trier le dossier", Close), choice, _summary, frame, buttons },
        });
        Content = card;
        DialogStyle.Appear(this, card);
        Refresh();
    }

    private void Refresh()
    {
        foreach (var (pill, mode) in _modes)
        {
            if (mode == _mode)
            {
                pill.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
            }
            else
            {
                pill.Background = Brushes.Transparent;
            }
        }

        _plan = FolderSort.Plan(_folder, _mode);
        _rows.Children.Clear();
        var groups = _plan.GroupBy(m => System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(m.To)!)).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase).ToList();
        foreach (var group in groups)
        {
            var examples = string.Join(", ", group.Take(3).Select(m => System.IO.Path.GetFileName(m.From))) + (group.Count() > 3 ? "…" : string.Empty);
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 4) };
            var count = new TextBlock { Text = $"{group.Count()} fichier{(group.Count() > 1 ? "s" : string.Empty)}", Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.Add(count);
            row.Children.Add(new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "📁 " + group.Key, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = examples, FontSize = 12, Opacity = 0.6, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            });
            _rows.Children.Add(row);
        }

        _summary.Text = _plan.Count == 0
            ? "Aucun fichier à trier directement dans ce dossier."
            : $"{_plan.Count} fichier{(_plan.Count > 1 ? "s" : string.Empty)} seront rangés dans {groups.Count} dossier{(groups.Count > 1 ? "s" : string.Empty)}. Les sous-dossiers existants ne sont pas touchés.";
        _apply.IsEnabled = _plan.Count > 0;
    }

    private void Apply()
    {
        try
        {
            _done = FolderSort.Apply(_plan);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            Refresh();
            return;
        }

        _summary.Text = $"{_done.Count} fichier{(_done.Count > 1 ? "s" : string.Empty)} rangé{(_done.Count > 1 ? "s" : string.Empty)}.";
        _apply.Visibility = Visibility.Collapsed;
        _undo.Visibility = Visibility.Visible;
        _cancel.Content = "Fermer";
        TrayBridge.Notify("Dossier trié", $"{_done.Count} fichiers rangés dans {System.IO.Path.GetFileName(_folder)}", _folder);
    }

    private void Undo()
    {
        try
        {
            FolderSort.Undo(_done);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _done = [];
        _undo.Visibility = Visibility.Collapsed;
        _apply.Visibility = Visibility.Visible;
        _cancel.Content = "Annuler";
        Refresh();
        _summary.Text = "Tri annulé : les fichiers sont revenus dans le dossier.";
    }
}
