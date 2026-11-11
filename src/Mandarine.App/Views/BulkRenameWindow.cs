using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mandarine.App.Services;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Renames the selected files from a template, with the list of new names shown live and an undo once done.</summary>
internal sealed class BulkRenameWindow : Window
{
    private readonly IReadOnlyList<string> _files;
    private readonly TextBox _template = new() { Text = "{nom}" };
    private readonly TextBox _start = new() { Text = "1", Width = 64 };
    private readonly TextBox _digits = new() { Text = "3", Width = 64 };
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary;
    private readonly Button _apply;
    private readonly Button _cancel;
    private readonly Button _undo;
    private IReadOnlyList<RenamePlan> _plan = [];
    private IReadOnlyList<(string From, string To)> _applied = [];

    public BulkRenameWindow(IReadOnlyList<string> files)
    {
        _files = files;
        Title = "Renommer en masse";
        DialogStyle.Frameless(this, 640);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        DialogStyle.StyleField(_template);
        DialogStyle.StyleField(_start, 34);
        DialogStyle.StyleField(_digits, 34);
        _template.TextChanged += (_, _) => Refresh();
        _start.TextChanged += (_, _) => Refresh();
        _digits.TextChanged += (_, _) => Refresh();

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (label, token) in new[] { ("Nom", "{nom}"), ("Numéro", "{n}"), ("Date", "{date}") })
        {
            chips.Children.Add(Chip(label, token));
        }

        chips.Children.Add(new TextBlock { Text = "Cliquez pour insérer à l'endroit du curseur", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.6 });

        var numbering = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { Label("Numéro de départ"), _start, Spacer(), Label("Chiffres"), _digits },
        };

        _summary = DialogStyle.Muted(string.Empty);
        _summary.Margin = new Thickness(0, 14, 0, 6);
        var list = new ScrollViewer { Content = _rows, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var listFrame = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), Child = list };
        listFrame.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        listFrame.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        _cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        _apply = DialogStyle.Button("Renommer", primary: true, isDefault: true);
        _undo = DialogStyle.Button("Annuler le renommage", primary: false);
        _undo.Visibility = Visibility.Collapsed;
        _apply.Click += (_, _) => Apply();
        _cancel.Click += (_, _) => Close();
        _undo.Click += (_, _) => Undo();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { _undo, _cancel, _apply } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children =
            {
                DialogStyle.Header("", $"{files.Count} fichiers", "Renommer en masse", Close),
                Label("Nouveau nom (l'extension est conservée)"),
                _template,
                chips,
                numbering,
                _summary,
                listFrame,
                buttons,
            },
        });
        Content = card;
        DialogStyle.Appear(this, card);
        Loaded += (_, _) =>
        {
            _template.Focus();
            _template.CaretIndex = _template.Text.Length;
        };
        Refresh();
    }

    private static TextBlock Label(string text)
    {
        var label = DialogStyle.Muted(text, 12.5);
        label.Margin = new Thickness(0, 0, 8, 6);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static FrameworkElement Spacer() => new Border { Width = 22 };

    private Border Chip(string label, string token)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 4, 12, 4),
            Margin = new Thickness(0, 0, 6, 0),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = label, FontSize = 12.5 },
        };
        chip.SetResourceReference(Border.BorderBrushProperty, "Accent");
        chip.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            var at = _template.SelectionStart;
            _template.Text = _template.Text.Remove(at, _template.SelectionLength).Insert(at, token);
            _template.Focus();
            _template.CaretIndex = at + token.Length;
        };
        chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
        chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
        return chip;
    }

    private void Refresh()
    {
        if (_rows is null || _summary is null)
        {
            return;
        }

        _plan = BulkRename.Plan(_files, new RenameOptions(_template.Text,
            int.TryParse(_start.Text, out var start) ? Math.Max(0, start) : 1,
            int.TryParse(_digits.Text, out var digits) ? digits : 3));

        _rows.Children.Clear();
        foreach (var item in _plan)
        {
            var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
            var before = new TextBlock { Text = Path.GetFileName(item.From), TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.65, FontSize = 12.5, ToolTip = item.From };
            var arrow = new TextBlock { Text = "→", Margin = new Thickness(10, 0, 10, 0), FontSize = 12.5, Opacity = 0.5 };
            var after = new TextBlock { Text = Path.GetFileName(item.To), TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12.5, FontWeight = item.Changes ? FontWeights.SemiBold : FontWeights.Normal };
            if (item.Problem is not null)
            {
                after.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B));
                after.ToolTip = item.Problem;
                after.Text = $"{Path.GetFileName(item.To)}  ⚠ {item.Problem}";
            }

            Grid.SetColumn(arrow, 1);
            Grid.SetColumn(after, 2);
            row.Children.Add(before);
            row.Children.Add(arrow);
            row.Children.Add(after);
            _rows.Children.Add(row);
        }

        var problems = _plan.Count(p => p.Problem is not null);
        var changes = _plan.Count(p => p.Changes);
        _summary.Text = problems > 0 ? $"{problems} conflit{(problems > 1 ? "s" : string.Empty)} à corriger avant de renommer."
            : changes == 0 ? "Aucun nom ne change pour l'instant."
            : $"{changes} fichier{(changes > 1 ? "s" : string.Empty)} sera{(changes > 1 ? "ont" : string.Empty)} renommé{(changes > 1 ? "s" : string.Empty)}.";
        _apply.IsEnabled = problems == 0 && changes > 0;
    }

    private void Apply()
    {
        try
        {
            _applied = BulkRename.Apply(_plan);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _summary.Text = $"{_applied.Count} fichier{(_applied.Count > 1 ? "s" : string.Empty)} renommé{(_applied.Count > 1 ? "s" : string.Empty)}.";
        _apply.Visibility = Visibility.Collapsed;
        _undo.Visibility = Visibility.Visible;
        _cancel.Content = "Fermer";
        _template.IsEnabled = _start.IsEnabled = _digits.IsEnabled = false;
        TrayBridge.Notify("Fichiers renommés", $"{_applied.Count} fichier{(_applied.Count > 1 ? "s" : string.Empty)} dans {Path.GetDirectoryName(_applied[0].To)}", Path.GetDirectoryName(_applied[0].To) ?? string.Empty);
    }

    private void Undo()
    {
        try
        {
            BulkRename.Undo(_applied);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _applied = [];
        _undo.Visibility = Visibility.Collapsed;
        _apply.Visibility = Visibility.Visible;
        _cancel.Content = "Annuler";
        _template.IsEnabled = _start.IsEnabled = _digits.IsEnabled = true;
        Refresh();
        _summary.Text = "Renommage annulé : les fichiers ont retrouvé leur nom.";
    }
}
