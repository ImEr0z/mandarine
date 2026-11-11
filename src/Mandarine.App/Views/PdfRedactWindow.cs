using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mandarine.App.Services;
using Mandarine.Core;
using PdfSharp.Pdf.IO;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Mandarine.App.Views;

/// <summary>Draw black boxes over what must disappear, then save a copy where the covered content is really gone.</summary>
internal sealed class PdfRedactWindow : Window
{
    private const int PreviewDpi = 110;
    private static readonly Color Orange = Color.FromRgb(0xF5, 0x8A, 0x1F);

    private readonly string _path;
    private readonly int _pageCount;
    private readonly Dictionary<int, List<Rect>> _marks = [];
    private readonly Image _image = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Border _sheet = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), Background = Brushes.White, CornerRadius = new CornerRadius(6), ClipToBounds = true };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), MinWidth = 82, TextAlignment = TextAlignment.Center, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Button _undo;
    private readonly Button _clear;
    private BitmapSource? _bitmap;
    private Size _area;
    private int _page;
    private Point? _from;
    private Rectangle? _drawing;
    private bool _busy;

    public PdfRedactWindow(string path)
    {
        _path = path;
        using (var document = PdfReader.Open(path, PdfDocumentOpenMode.Import))
        {
            _pageCount = document.PageCount;
        }

        Title = $"Caviarder – {Path.GetFileName(path)}";
        Width = 980;
        Height = 760;
        MinWidth = 700;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");
        UseLayoutRounding = true;

        var previous = PdfEditorWindow.Tool("", null, "Page précédente (Page haut)", compact: true);
        previous.Click += (_, _) => ShowPage(_page - 1);
        var next = PdfEditorWindow.Tool("", null, "Page suivante (Page bas)", compact: true);
        next.Click += (_, _) => ShowPage(_page + 1);
        _undo = (Button)PdfEditorWindow.Tool("", "Annuler", "Retirer la dernière zone (Ctrl+Z)");
        _undo.Click += (_, _) => Undo();
        _clear = (Button)PdfEditorWindow.Tool("", "Vider la page", "Retirer toutes les zones de cette page");
        _clear.Click += (_, _) =>
        {
            _marks.Remove(_page);
            Refresh();
        };
        var save = (Button)PdfEditorWindow.Tool("", "Enregistrer une copie", "Crée « … - caviardé.pdf » à côté de l'original", accent: true);
        save.Click += async (_, _) => await SaveAsync();

        var left = new StackPanel { Orientation = Orientation.Horizontal, Children = { previous, _pageLabel, next } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { _undo, _clear, save } };
        var hint = new TextBlock { Text = "Tracez un rectangle sur ce qu'il faut cacher", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.65 };
        var bar = new DockPanel { Margin = new Thickness(14, 10, 14, 10) };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(left);
        bar.Children.Add(right);
        bar.Children.Add(hint);
        var top = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        top.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        var footer = new Border { Child = _status, Padding = new Thickness(16, 8, 16, 8), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        _status.SetResourceReference(ForegroundProperty, "Text.Secondary");

        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(_image);
        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += OnUp;
        _sheet.Child = _canvas;
        _sheet.BorderThickness = new Thickness(1);
        _sheet.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        var area = new Grid { Children = { _sheet } };
        area.SetResourceReference(BackgroundProperty, "Tile.Background");
        area.SizeChanged += (_, e) =>
        {
            _area = e.NewSize;
            Fit();
        };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(area);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.PageUp)
            {
                ShowPage(_page - 1);
            }
            else if (e.Key == Key.PageDown)
            {
                ShowPage(_page + 1);
            }
            else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Undo();
            }
            else if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        Loaded += (_, _) => ShowPage(0);
    }

    private async void ShowPage(int page)
    {
        if (page < 0 || page >= _pageCount || _busy)
        {
            return;
        }

        _page = page;
        _pageLabel.Text = $"Page {page + 1} / {_pageCount}";
        try
        {
            var bitmap = await Task.Run(() =>
            {
                using var stream = new MemoryStream(Raster.PdfPageToPng(_path, page, PreviewDpi));
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return (BitmapSource)image;
            });
            if (_page != page)
            {
                return;
            }

            _bitmap = bitmap;
            _image.Source = bitmap;
            Fit();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(this, "Impossible d'afficher cette page : " + ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Fit()
    {
        if (_bitmap is null || _area.Width < 100)
        {
            return;
        }

        var scale = Math.Min((_area.Width - 50) / _bitmap.PixelWidth, (_area.Height - 50) / _bitmap.PixelHeight);
        _canvas.Width = _image.Width = _bitmap.PixelWidth * scale;
        _canvas.Height = _image.Height = _bitmap.PixelHeight * scale;
        Refresh();
    }

    /// <summary>Redraws the boxes of the current page and the counters.</summary>
    private void Refresh()
    {
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is Rectangle { Tag: "mark" })
            {
                _canvas.Children.RemoveAt(i);
            }
        }

        foreach (var mark in _marks.GetValueOrDefault(_page) ?? [])
        {
            var box = new Rectangle
            {
                Width = mark.Width * _canvas.Width,
                Height = mark.Height * _canvas.Height,
                Fill = Brushes.Black,
                Stroke = new SolidColorBrush(Orange),
                StrokeThickness = 1.5,
                RadiusX = 2,
                RadiusY = 2,
                Tag = "mark",
                Cursor = Cursors.Hand,
            };
            var remove = new MenuItem { Header = "Retirer cette zone" };
            var captured = mark;
            remove.Click += (_, _) =>
            {
                _marks[_page].Remove(captured);
                Refresh();
            };
            box.ContextMenu = new ContextMenu { Items = { remove } };
            Canvas.SetLeft(box, mark.X * _canvas.Width);
            Canvas.SetTop(box, mark.Y * _canvas.Height);
            _canvas.Children.Add(box);
        }

        var total = _marks.Values.Sum(list => list.Count);
        var pages = _marks.Count(pair => pair.Value.Count > 0);
        _undo.IsEnabled = _marks.GetValueOrDefault(_page)?.Count > 0;
        _clear.IsEnabled = _undo.IsEnabled;
        _status.Text = total == 0
            ? "Aucune zone pour l'instant. Clic droit sur une zone pour la retirer."
            : $"{total} zone{(total > 1 ? "s" : string.Empty)} sur {pages} page{(pages > 1 ? "s" : string.Empty)}. "
              + "Les pages caviardées deviennent des images : le texte caché est supprimé pour de bon, mais n'y est plus sélectionnable.";
    }

    private void Undo()
    {
        if (_marks.GetValueOrDefault(_page) is { Count: > 0 } list)
        {
            list.RemoveAt(list.Count - 1);
            Refresh();
        }
    }

    // ---- Drawing

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _from = e.GetPosition(_canvas);
        _drawing = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), Stroke = new SolidColorBrush(Orange), StrokeThickness = 1.5, IsHitTestVisible = false };
        _canvas.Children.Add(_drawing);
        _canvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_from is not { } from || _drawing is null)
        {
            return;
        }

        var to = Clamp(e.GetPosition(_canvas));
        Canvas.SetLeft(_drawing, Math.Min(from.X, to.X));
        Canvas.SetTop(_drawing, Math.Min(from.Y, to.Y));
        _drawing.Width = Math.Abs(to.X - from.X);
        _drawing.Height = Math.Abs(to.Y - from.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_from is not { } from || _drawing is null)
        {
            return;
        }

        var to = Clamp(e.GetPosition(_canvas));
        _canvas.ReleaseMouseCapture();
        _canvas.Children.Remove(_drawing);
        _drawing = null;
        _from = null;
        var rect = new Rect(
            Math.Min(from.X, to.X) / _canvas.Width, Math.Min(from.Y, to.Y) / _canvas.Height,
            Math.Abs(to.X - from.X) / _canvas.Width, Math.Abs(to.Y - from.Y) / _canvas.Height);
        if (rect.Width > 0.004 && rect.Height > 0.004)
        {
            if (!_marks.TryGetValue(_page, out var list))
            {
                _marks[_page] = list = [];
            }

            list.Add(rect);
        }

        Refresh();
    }

    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, _canvas.Width), Math.Clamp(point.Y, 0, _canvas.Height));

    // ---- Saving

    private async Task SaveAsync()
    {
        var marks = _marks.SelectMany(pair => pair.Value.Select(r => new PdfRedaction(pair.Key, r.X, r.Y, r.Width, r.Height))).ToList();
        if (marks.Count == 0)
        {
            MessageBox.Show(this, "Tracez d'abord au moins une zone à cacher.", "Mandarine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _busy = true;
        IsEnabled = false;
        var output = OutputPaths.ForFile(_path, "pdf", null, "caviardé");
        try
        {
            var pages = await Task.Run(() => PdfRedactor.Save(_path, output, marks));
            TrayBridge.Notify("PDF caviardé",
                $"{marks.Count} zone{(marks.Count > 1 ? "s" : string.Empty)} sur {pages} page{(pages > 1 ? "s" : string.Empty)} : {Path.GetFileName(output)}", output);
            Close();
        }
        catch (Exception ex)
        {
            OutputPaths.Discard(output);
            ErrorLog.Write(ex);
            _busy = false;
            IsEnabled = true;
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
