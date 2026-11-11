using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Mandarine.App.Services;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Path = System.Windows.Shapes.Path;

namespace Mandarine.App.Views;

/// <summary>Arrows, boxes and text on a picture, or blur / hide a zone, then save a copy next to the original.</summary>
internal sealed class ImageAnnotateWindow : Window
{
    private enum Tool { Arrow, Box, Text, Blur, Mask }

    private abstract record Mark;

    private sealed record ArrowMark(Point From, Point To, Color Color, double Width) : Mark;

    private sealed record BoxMark(Rect Area, Color Color, double Width) : Mark;

    private sealed record NoteMark(Point At, string Text, Color Color, double Size) : Mark;

    private sealed record BlurMark(Rect Area) : Mark;

    private sealed record MaskMark(Rect Area) : Mark;

    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xE5, 0x39, 0x35), Color.FromRgb(0xF5, 0x8A, 0x1F), Color.FromRgb(0xF9, 0xD0, 0x1C),
        Color.FromRgb(0x2E, 0xA0, 0x43), Color.FromRgb(0x2F, 0x8F, 0xEA), Colors.Black, Colors.White,
    ];

    private static readonly double[] WidthFactors = [1, 1.8, 3];

    private readonly string _path;
    private readonly BitmapSource _source;
    private readonly List<Mark> _marks = [];
    private readonly Stack<Mark> _undone = new();
    private readonly Dictionary<Rect, BitmapSource> _pixelated = [];
    private readonly Image _image = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, ClipToBounds = true, Cursor = Cursors.Cross };
    private readonly Border _stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
    private readonly TextBlock _hint = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Dictionary<Tool, ToggleButton> _toolButtons = [];
    private readonly List<Border> _swatches = [];
    private readonly List<Border> _pills = [];
    private readonly Button _undo;
    private Tool _tool;
    private Color _color = Palette[0];
    private int _width = 1;
    private double _scale = 1;
    private Point? _from;
    private FrameworkElement? _temporary;

    public ImageAnnotateWindow(string path, bool blur)
    {
        _path = path;
        _source = ImageCropWindow.Load(path);
        Title = $"{(blur ? "Flouter / masquer" : "Annoter")} – {System.IO.Path.GetFileName(path)}";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(_source.PixelWidth * 0.8 + 80, 820, work.Width * 0.9);
        Height = Math.Clamp(_source.PixelHeight * 0.8 + 190, 560, work.Height * 0.92);
        MinWidth = 760;
        MinHeight = 480;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");
        UseLayoutRounding = true;

        // ---- Tools
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (tool, glyph, label, tip) in new[]
                 {
                     (Tool.Arrow, "", "Flèche", "Glissez de la queue vers la pointe"),
                     (Tool.Box, "", "Cadre", "Glissez pour encadrer"),
                     (Tool.Text, "", "Texte", "Cliquez où poser le texte"),
                     (Tool.Blur, "", "Flouter", "Glissez sur la zone à flouter"),
                     (Tool.Mask, "", "Masquer", "Glissez sur la zone à cacher en noir"),
                 })
        {
            var button = (ToggleButton)PdfEditorWindow.Tool(glyph, label, tip, toggle: true);
            button.Click += (_, _) => Select(tool);
            _toolButtons[tool] = button;
            tools.Children.Add(button);
        }

        // ---- Colour and thickness (apply to the next marks)
        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) };
        foreach (var color in Palette)
        {
            var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(color), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)) };
            var swatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Padding = new Thickness(3), Margin = new Thickness(0, 0, 2, 0), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = dot, ToolTip = "Couleur" };
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _color = color;
                RefreshSwatches();
            };
            _swatches.Add(swatch);
            colors.Children.Add(swatch);
        }

        var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
        var thickness = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Child = pillRow };
        thickness.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        foreach (var (label, index) in new[] { ("Fin", 0), ("Moyen", 1), ("Épais", 2) })
        {
            var pill = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(11, 4, 11, 4), Cursor = Cursors.Hand, Child = new TextBlock { Text = label, FontSize = 12.5 } };
            pill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _width = index;
                RefreshSwatches();
            };
            _pills.Add(pill);
            pillRow.Children.Add(pill);
        }

        _undo = (Button)PdfEditorWindow.Tool("", "Annuler", "Retirer la dernière marque (Ctrl+Z)");
        _undo.Click += (_, _) => Undo();
        var save = (Button)PdfEditorWindow.Tool("", "Enregistrer une copie", "Crée une copie à côté de l'original (Ctrl+S)", accent: true);
        save.Click += (_, _) => Save();

        var right = new StackPanel { Orientation = Orientation.Horizontal, Children = { _undo, save } };
        var row1 = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row1.Children.Add(right);
        row1.Children.Add(tools);

        // Second row: colour and thickness of the next marks.
        colors.Margin = new Thickness(0, 0, 18, 0);
        var row2Label = DialogStyle.Muted("Couleur", 12);
        row2Label.VerticalAlignment = VerticalAlignment.Center;
        row2Label.Margin = new Thickness(8, 0, 10, 0);
        var widthLabel = DialogStyle.Muted("Épaisseur", 12);
        widthLabel.VerticalAlignment = VerticalAlignment.Center;
        widthLabel.Margin = new Thickness(0, 0, 8, 0);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { row2Label, colors, widthLabel, thickness } };
        var bar = new StackPanel { Margin = new Thickness(12, 8, 12, 8), Children = { row1, row2 } };
        var top = new Border { Child = bar, BorderThickness = new Thickness(0, 0, 0, 1) };
        top.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        var footer = new Border { Child = _hint, Padding = new Thickness(16, 8, 16, 8), BorderThickness = new Thickness(0, 1, 0, 0) };
        footer.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");

        // ---- Picture
        _image.Source = _source;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(_image);
        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += OnUp;
        _stage.Child = _canvas;
        var area = new Grid { Children = { _stage } };
        area.SetResourceReference(BackgroundProperty, "Tile.Background");
        area.SizeChanged += (_, e) => Fit(e.NewSize);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(footer);
        root.Children.Add(area);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.Z:
                        Undo();
                        break;
                    case Key.Y:
                        Redo();
                        break;
                    case Key.S:
                        Save();
                        break;
                }
            }
        };

        Select(blur ? Tool.Blur : Tool.Arrow);
        RefreshSwatches();
        Loaded += (_, _) => Fit(area.RenderSize);
    }

    // ---- Tool state

    private void Select(Tool tool)
    {
        _tool = tool;
        foreach (var (key, button) in _toolButtons)
        {
            button.IsChecked = key == tool;
        }

        _hint.Text = tool switch
        {
            Tool.Arrow => "Flèche : glissez de la queue vers la pointe. Clic droit sur une marque pour la retirer.",
            Tool.Box => "Cadre : glissez pour encadrer. Clic droit sur une marque pour la retirer.",
            Tool.Text => "Texte : cliquez à l'endroit voulu, puis tapez. Clic droit sur une marque pour la retirer.",
            Tool.Blur => "Flou : glissez sur la zone à rendre illisible (pixellisation, définitive dans la copie).",
            _ => "Masquer : glissez sur la zone à cacher en noir.",
        };
    }

    private void RefreshSwatches()
    {
        foreach (var (swatch, color) in _swatches.Zip(Palette))
        {
            if (color == _color)
            {
                swatch.SetResourceReference(Border.BorderBrushProperty, "Accent");
            }
            else
            {
                swatch.BorderBrush = Brushes.Transparent;
            }
        }

        for (var i = 0; i < _pills.Count; i++)
        {
            if (i == _width)
            {
                _pills[i].SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
            }
            else
            {
                _pills[i].Background = Brushes.Transparent;
            }
        }
    }

    /// <summary>Stroke width in picture pixels, relative to the size of the picture so it is visible on any photo.</summary>
    private double StrokeWidth => Math.Max(2, Math.Max(_source.PixelWidth, _source.PixelHeight) / 400.0) * WidthFactors[_width];

    private double TextSize => Math.Max(16, _source.PixelWidth / 38.0) * (1 + _width * 0.5);

    // ---- Layout

    private void Fit(Size available)
    {
        var width = Math.Max(60, available.Width - 40);
        var height = Math.Max(60, available.Height - 40);
        _scale = Math.Min(width / _source.PixelWidth, height / _source.PixelHeight);
        _canvas.Width = _image.Width = _source.PixelWidth * _scale;
        _canvas.Height = _image.Height = _source.PixelHeight * _scale;
        Rebuild();
    }

    private void Rebuild()
    {
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is FrameworkElement { Tag: Mark })
            {
                _canvas.Children.RemoveAt(i);
            }
        }

        foreach (var mark in _marks)
        {
            var element = Element(mark);
            var remove = new MenuItem { Header = "Retirer cette marque" };
            var captured = mark;
            remove.Click += (_, _) =>
            {
                _marks.Remove(captured);
                Rebuild();
            };
            element.ContextMenu = new ContextMenu { Items = { remove } };
            _canvas.Children.Add(element);
        }

        _undo.IsEnabled = _marks.Count > 0;
    }

    /// <summary>The on-screen element of a mark, placed on the canvas at the current zoom.</summary>
    private FrameworkElement Element(Mark mark)
    {
        FrameworkElement element;
        double left, top;
        switch (mark)
        {
            case ArrowMark arrow:
                element = new Path { Data = ArrowGeometry(Scale(arrow.From), Scale(arrow.To), arrow.Width * _scale), Stroke = new SolidColorBrush(arrow.Color), Fill = new SolidColorBrush(arrow.Color), StrokeThickness = arrow.Width * _scale, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round };
                (left, top) = (0, 0);
                break;
            case BoxMark box:
                element = new Rectangle { Width = box.Area.Width * _scale, Height = box.Area.Height * _scale, Stroke = new SolidColorBrush(box.Color), StrokeThickness = box.Width * _scale, RadiusX = 2, RadiusY = 2 };
                (left, top) = (box.Area.X * _scale, box.Area.Y * _scale);
                break;
            case NoteMark note:
                element = new TextBlock { Text = note.Text, FontSize = note.Size * _scale, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(note.Color) };
                (left, top) = (note.At.X * _scale, note.At.Y * _scale);
                break;
            case BlurMark blur:
                element = new Image { Source = Pixelate(blur.Area), Width = blur.Area.Width * _scale, Height = blur.Area.Height * _scale, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(element, BitmapScalingMode.NearestNeighbor);
                (left, top) = (blur.Area.X * _scale, blur.Area.Y * _scale);
                break;
            default:
                var mask = (MaskMark)mark;
                element = new Rectangle { Width = mask.Area.Width * _scale, Height = mask.Area.Height * _scale, Fill = Brushes.Black };
                (left, top) = (mask.Area.X * _scale, mask.Area.Y * _scale);
                break;
        }

        element.Tag = mark;
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }

    private Point Scale(Point point) => new(point.X * _scale, point.Y * _scale);

    /// <summary>Shaft and a filled head, in the units of <paramref name="from"/> and <paramref name="to"/>.</summary>
    private static Geometry ArrowGeometry(Point from, Point to, double width)
    {
        var direction = to - from;
        var length = direction.Length;
        if (length < 1)
        {
            return Geometry.Empty;
        }

        direction.Normalize();
        var head = Math.Min(length * 0.6, Math.Max(width * 4.5, 12));
        var normal = new Vector(-direction.Y, direction.X);
        var baseCenter = to - direction * head;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(from, false, false);
            context.LineTo(baseCenter, true, true);
            context.BeginFigure(to, true, true);
            context.LineTo(baseCenter + normal * head * 0.5, true, false);
            context.LineTo(baseCenter - normal * head * 0.5, true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>The zone redrawn as large blocks (about ten across): unreadable, and no blur to undo.</summary>
    private BitmapSource Pixelate(Rect area)
    {
        if (_pixelated.TryGetValue(area, out var cached))
        {
            return cached;
        }

        var x = Math.Clamp((int)area.X, 0, _source.PixelWidth - 1);
        var y = Math.Clamp((int)area.Y, 0, _source.PixelHeight - 1);
        var width = Math.Clamp((int)area.Width, 1, _source.PixelWidth - x);
        var height = Math.Clamp((int)area.Height, 1, _source.PixelHeight - y);
        var block = Math.Max(6, Math.Max(width, height) / 10);
        BitmapSource result = new CroppedBitmap(_source, new Int32Rect(x, y, width, height));
        if (block < Math.Min(width, height))
        {
            result = new TransformedBitmap(result, new ScaleTransform(1.0 / block, 1.0 / block));
        }
        else
        {
            result = new TransformedBitmap(result, new ScaleTransform(1.0 / Math.Max(width, height), 1.0 / Math.Max(width, height)));
        }

        result.Freeze();
        _pixelated[area] = result;
        return result;
    }

    // ---- Drawing

    private Point PixelAt(MouseEventArgs e)
    {
        var at = e.GetPosition(_canvas);
        return new Point(Math.Clamp(at.X / _scale, 0, _source.PixelWidth), Math.Clamp(at.Y / _scale, 0, _source.PixelHeight));
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        var at = PixelAt(e);
        if (_tool == Tool.Text)
        {
            var text = InputDialog.Ask("Texte", new ActionPrompt("Texte à ajouter", "Il sera posé à l'endroit cliqué"), "");
            if (text is not null)
            {
                Add(new NoteMark(at, text, _color, TextSize));
            }

            return;
        }

        _from = at;
        _canvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (_from is not { } from)
        {
            return;
        }

        if (_temporary is not null)
        {
            _canvas.Children.Remove(_temporary);
        }

        // Live preview: for a blur, a light veil (the real pixels are computed on release).
        var mark = Build(from, PixelAt(e), preview: true);
        _temporary = mark is null ? null : Element(mark);
        if (_temporary is not null)
        {
            _temporary.IsHitTestVisible = false;
            _canvas.Children.Add(_temporary);
        }
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_from is not { } from)
        {
            return;
        }

        _canvas.ReleaseMouseCapture();
        _from = null;
        if (_temporary is not null)
        {
            _canvas.Children.Remove(_temporary);
            _temporary = null;
        }

        if (Build(from, PixelAt(e), preview: false) is { } mark)
        {
            Add(mark);
        }
    }

    private Mark? Build(Point from, Point to, bool preview)
    {
        var area = new Rect(from, to);
        if (_tool == Tool.Arrow)
        {
            return (to - from).Length < 8 ? null : new ArrowMark(from, to, _color, StrokeWidth);
        }

        if (area.Width < 6 || area.Height < 6)
        {
            return null;
        }

        return _tool switch
        {
            Tool.Box => new BoxMark(area, _color, StrokeWidth),
            Tool.Blur => new BlurMark(area),
            _ => new MaskMark(area),
        };
    }

    private void Add(Mark mark)
    {
        _marks.Add(mark);
        _undone.Clear();
        Rebuild();
    }

    private void Undo()
    {
        if (_marks.Count > 0)
        {
            _undone.Push(_marks[^1]);
            _marks.RemoveAt(_marks.Count - 1);
            Rebuild();
        }
    }

    private void Redo()
    {
        if (_undone.Count > 0)
        {
            _marks.Add(_undone.Pop());
            Rebuild();
        }
    }

    // ---- Saving

    private BitmapSource Compose()
    {
        var width = _source.PixelWidth;
        var height = _source.PixelHeight;
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(_source, new Rect(0, 0, width, height));
            foreach (var mark in _marks)
            {
                switch (mark)
                {
                    case ArrowMark arrow:
                        var brush = new SolidColorBrush(arrow.Color);
                        dc.DrawGeometry(brush, new Pen(brush, arrow.Width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round }, ArrowGeometry(arrow.From, arrow.To, arrow.Width));
                        break;
                    case BoxMark box:
                        dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(box.Color), box.Width), box.Area, 2, 2);
                        break;
                    case NoteMark note:
                        var text = new FormattedText(note.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), note.Size, new SolidColorBrush(note.Color), 1.0);
                        dc.DrawText(text, note.At);
                        break;
                    case BlurMark blur:
                        dc.DrawImage(Pixelate(blur.Area), new Rect(blur.Area.X, blur.Area.Y, Math.Min(blur.Area.Width, width - blur.Area.X), Math.Min(blur.Area.Height, height - blur.Area.Y)));
                        break;
                    case MaskMark mask:
                        dc.DrawRectangle(Brushes.Black, null, mask.Area);
                        break;
                }
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        return target;
    }

    private void Save()
    {
        if (_marks.Count == 0)
        {
            MessageBox.Show(this, "Ajoutez d'abord au moins une marque.", "Mandarine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var extension = System.IO.Path.GetExtension(_path).TrimStart('.').ToLowerInvariant();
        BitmapEncoder encoder = extension switch
        {
            "jpg" or "jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
            "bmp" => new BmpBitmapEncoder(),
            "gif" => new GifBitmapEncoder(),
            "tif" or "tiff" => new TiffBitmapEncoder(),
            _ => new PngBitmapEncoder(),
        };
        var hides = _marks.All(m => m is BlurMark or MaskMark);
        var output = OutputPaths.ForFile(_path, extension, null, hides ? "masqué" : "annoté");
        try
        {
            encoder.Frames.Add(BitmapFrame.Create(Compose()));
            using var stream = File.Create(output);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            OutputPaths.Discard(output);
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TrayBridge.Notify(hides ? "Zones masquées" : "Image annotée",
            $"{_marks.Count} marque{(_marks.Count > 1 ? "s" : string.Empty)} : {System.IO.Path.GetFileName(_path)} → {System.IO.Path.GetFileName(output)}", output);
        Close();
    }
}
