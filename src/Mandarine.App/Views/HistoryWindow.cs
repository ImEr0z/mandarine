using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Mandarine.App.Services;

namespace Mandarine.App.Views;

/// <summary>The latest files Mandarine created, each with a thumbnail. Click one to open it.</summary>
internal sealed class HistoryWindow : Window
{
    public HistoryWindow()
    {
        Title = "Conversions récentes";
        DialogStyle.Frameless(this, 560);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var rows = new StackPanel();
        var entries = History.Load();
        foreach (var entry in entries)
        {
            rows.Children.Add(Row(entry));
        }

        UIElement body = entries.Count == 0
            ? DialogStyle.Muted("Rien pour l'instant : les fichiers créés par Mandarine apparaîtront ici.")
            : new ScrollViewer { Content = rows, MaxHeight = 440, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var close = DialogStyle.Button("Fermer", primary: false, isCancel: true);
        close.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0), Children = { close } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children = { DialogStyle.Header("", "Mandarine", "Conversions récentes", Close), body, buttons },
        });
        Content = card;
        DialogStyle.Appear(this, card);
    }

    private static Border Row(HistoryEntry entry)
    {
        var thumbnail = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 12, 0) };
        _ = ShowThumbnailAsync(thumbnail, entry.Path);

        var name = new TextBlock { Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(entry.Path)), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        var detail = DialogStyle.Muted($"{entry.Time:d MMM, HH:mm} · {Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(entry.Path))}");
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        detail.TextWrapping = TextWrapping.NoWrap;

        var folder = new TextBlock { Text = "", FontFamily = DialogStyle.IconFont, FontSize = 15, Padding = new Thickness(10), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, ToolTip = "Afficher dans l'Explorateur" };
        folder.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        folder.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Launch("explorer.exe", $"/select,\"{entry.Path}\"");
        };

        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, detail } };
        var grid = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(thumbnail, Dock.Left);
        DockPanel.SetDock(folder, Dock.Right);
        grid.Children.Add(thumbnail);
        grid.Children.Add(folder);
        grid.Children.Add(texts);

        var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(8), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = grid };
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, _) => Launch(entry.Path, null);
        return row;
    }

    private static async Task ShowThumbnailAsync(Image image, string path)
    {
        var bitmap = await Task.Run(() => Thumbnails.Get(path, 96));
        if (bitmap is not null)
        {
            image.Source = bitmap;
        }
    }

    private static void Launch(string file, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, Arguments = arguments ?? string.Empty })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }
}
