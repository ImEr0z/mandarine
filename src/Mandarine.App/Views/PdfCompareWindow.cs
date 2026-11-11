using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Differences of text between two versions of a PDF: removed lines in red, added lines in green, by page.</summary>
internal sealed class PdfCompareWindow : Window
{
    private readonly StackPanel _list = new() { Margin = new Thickness(16) };
    private readonly TextBlock _summary = new() { Margin = new Thickness(16, 12, 16, 4), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };

    public PdfCompareWindow(string oldPath, string newPath)
    {
        Title = $"Comparer – {Path.GetFileName(oldPath)} / {Path.GetFileName(newPath)}";
        Width = 820;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");

        var legend = new TextBlock
        {
            Margin = new Thickness(16, 0, 16, 8),
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Text = $"Avant : {Path.GetFileName(oldPath)}   ·   Après : {Path.GetFileName(newPath)}",
        };
        var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var top = new StackPanel { Children = { _summary, legend } };
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(scroll);
        Content = root;

        _summary.Text = "Comparaison en cours…";
        Loaded += async (_, _) =>
        {
            try
            {
                Show(await Task.Run(() => PdfCompare.Compare(oldPath, newPath)));
            }
            catch (Exception ex)
            {
                _summary.Text = "Comparaison impossible : " + ex.Message;
            }
        };
    }

    private void Show(PdfComparison comparison)
    {
        var added = comparison.Differences.Count(d => d.Added);
        var removed = comparison.Differences.Count - added;
        var pages = comparison.OldPages == comparison.NewPages
            ? $"{comparison.OldPages} page{(comparison.OldPages > 1 ? "s" : string.Empty)}"
            : $"{comparison.OldPages} → {comparison.NewPages} pages";
        _summary.Text = comparison.Differences.Count == 0
            ? $"Aucune différence de texte ({pages})."
            : $"{removed} ligne{(removed > 1 ? "s" : string.Empty)} supprimée{(removed > 1 ? "s" : string.Empty)}, {added} ajoutée{(added > 1 ? "s" : string.Empty)} ({pages}).";

        foreach (var group in comparison.Differences.GroupBy(d => d.Page))
        {
            _list.Children.Add(new TextBlock { Text = $"Page {group.Key}", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 4) });
            foreach (var difference in group)
            {
                var color = difference.Added ? Color.FromArgb(70, 0x2E, 0xA0, 0x43) : Color.FromArgb(70, 0xD1, 0x3B, 0x2F);
                _list.Children.Add(new Border
                {
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 1, 0, 1),
                    Child = new TextBlock { Text = (difference.Added ? "+  " : "−  ") + difference.Text, TextWrapping = TextWrapping.Wrap },
                });
            }
        }
    }
}
