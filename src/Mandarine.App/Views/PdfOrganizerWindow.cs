using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mandarine.App.Services;
using Mandarine.Core;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Mandarine.App.Views;

/// <summary>Thumbnails of every page: move, rotate, duplicate or delete them, then save a new PDF next to the original.</summary>
internal sealed class PdfOrganizerWindow : Window
{
    private sealed class Entry(int source)
    {
        public int Source { get; } = source;

        public int Rotation { get; set; }
    }

    private readonly string _path;
    private readonly List<Entry> _entries;
    private readonly Dictionary<int, BitmapSource> _thumbnails = [];
    private readonly WrapPanel _wall = new() { Margin = new Thickness(12) };
    private readonly TextBlock _info = new() { VerticalAlignment = VerticalAlignment.Center };
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public PdfOrganizerWindow(string path)
    {
        _path = path;
        using (var document = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            _entries = Enumerable.Range(0, document.PageCount).Select(i => new Entry(i)).ToList();
        }

        Title = $"Réorganiser les pages – {Path.GetFileName(path)}";
        Width = 900;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");

        var save = DialogStyle.Button("Enregistrer une copie", primary: true);
        save.Margin = new Thickness(0, 0, 14, 0);
        save.Click += (_, _) => Save();
        var bar = new DockPanel { Margin = new Thickness(12, 10, 12, 10) };
        DockPanel.SetDock(save, Dock.Left);
        bar.Children.Add(save);
        bar.Children.Add(_info);

        var scroll = new ScrollViewer { Content = _wall, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.SetResourceReference(BackgroundProperty, "Tile.Background");
        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        root.Children.Add(scroll);
        Content = root;

        Rebuild();
        Loaded += (_, _) => _ = LoadThumbnailsAsync();
    }

    private async Task LoadThumbnailsAsync()
    {
        var count = _entries.Count;
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var bitmap = await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(Raster.PdfPageToPng(_path, index, 40));
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return (BitmapSource)image;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                    return null;
                }
            });
            if (bitmap is not null)
            {
                _thumbnails[index] = bitmap;
                foreach (var image in _wall.Children.OfType<Border>().Select(b => b.Tag as (Entry Entry, Image Image)?).Where(t => t?.Entry.Source == index))
                {
                    image!.Value.Image.Source = bitmap;
                }
            }
        }
    }

    private void Rebuild()
    {
        _wall.Children.Clear();
        for (var i = 0; i < _entries.Count; i++)
        {
            _wall.Children.Add(Card(i));
        }

        _info.Text = $"{_entries.Count} page{(_entries.Count > 1 ? "s" : string.Empty)} – déplacez, tournez, dupliquez ou supprimez, puis enregistrez.";
    }

    private Border Card(int position)
    {
        var entry = _entries[position];
        var image = new Image { Width = 130, Height = 170, Stretch = Stretch.Uniform, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(entry.Rotation) };
        if (_thumbnails.TryGetValue(entry.Source, out var thumbnail))
        {
            image.Source = thumbnail;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(Small("", "Déplacer vers le début", () => Move(position, -1), position > 0));
        buttons.Children.Add(Small("", "Déplacer vers la fin", () => Move(position, 1), position < _entries.Count - 1));
        buttons.Children.Add(Small("", "Tourner de 90°", () => { entry.Rotation = (entry.Rotation + 90) % 360; Rebuild(); }, true));
        buttons.Children.Add(Small("", "Dupliquer la page", () => { _entries.Insert(position + 1, new Entry(entry.Source) { Rotation = entry.Rotation }); Rebuild(); }, true));
        buttons.Children.Add(Small("", "Supprimer la page", () => { _entries.RemoveAt(position); Rebuild(); }, _entries.Count > 1));

        var card = new Border
        {
            Margin = new Thickness(6),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Tag = (entry, image),
            Child = new StackPanel
            {
                Children =
                {
                    new Border { Child = image, Background = Brushes.White, ClipToBounds = true },
                    new TextBlock { Text = $"{position + 1}", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0), Opacity = 0.7 },
                    buttons,
                },
            },
        };
        card.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        return card;
    }

    private static Button Small(string glyph, string tip, Action action, bool enabled)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 13 },
            ToolTip = tip,
            Width = 26,
            Height = 26,
            Margin = new Thickness(1),
            IsEnabled = enabled,
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Move(int position, int delta)
    {
        var entry = _entries[position];
        _entries.RemoveAt(position);
        _entries.Insert(position + delta, entry);
        Rebuild();
    }

    private void Save()
    {
        var output = OutputPaths.ForFile(_path, "pdf", null, "réorganisé");
        try
        {
            using var source = PdfReader.Open(_path, PdfDocumentOpenMode.Import);
            using var result = new PdfDocument();
            foreach (var entry in _entries)
            {
                var page = result.AddPage(source.Pages[entry.Source]);
                page.Rotate = (page.Rotate + entry.Rotation) % 360;
            }

            result.Save(output);
        }
        catch (Exception ex)
        {
            OutputPaths.Discard(output);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TrayBridge.Notify("Pages réorganisées", $"{Path.GetFileName(_path)} → {Path.GetFileName(output)} ({_entries.Count} pages)", output);
        Close();
    }
}
