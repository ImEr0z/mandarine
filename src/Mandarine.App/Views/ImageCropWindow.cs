using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mandarine.App.Services;
using Mandarine.Core;
using Line = System.Windows.Shapes.Line;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Mandarine.App.Views;

/// <summary>Crop an image by hand: drag the frame or its handles, then save a copy next to the original.</summary>
internal sealed class ImageCropWindow : Window
{
    public static readonly string[] Extensions = ["png", "jpg", "jpeg", "bmp", "gif", "tif", "tiff"];

    private const double HandleSize = 11, MinSize = 8;
    private static readonly Color Orange = Color.FromRgb(0xF5, 0x8A, 0x1F);

    private enum Grip { None, Move, NW, N, NE, E, SE, S, SW, W, New }

    private readonly string _path;
    private readonly BitmapSource _source;
    private readonly Image _image = new();
    private readonly Canvas _canvas = new() { ClipToBounds = true };
    private readonly Border _stage = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20) };
    private readonly System.Windows.Shapes.Path _dim = new() { Fill = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsHitTestVisible = false };
    private readonly Rectangle _frame = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false };
    private readonly List<Line> _thirds = [];
    private readonly Dictionary<Grip, Rectangle> _handles = [];
    private readonly TextBlock _size = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private Rect _selection;
    private double _scale = 1;
    private Grip _grip;
    private double? _ratio;
    private readonly List<(Border Pill, double? Ratio)> _ratios = [];
    private Point _anchor;
    private Rect _start;

    public ImageCropWindow(string path)
    {
        _path = path;
        _source = Load(path);
        Title = $"Recadrer – {Path.GetFileName(path)}";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Clamp(_source.PixelWidth * 0.8 + 80, 640, work.Width * 0.9);
        Height = Math.Clamp(_source.PixelHeight * 0.8 + 140, 480, work.Height * 0.92);
        MinWidth = 520;
        MinHeight = 380;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");

        var save = new Button { Content = "Enregistrer", Padding = new Thickness(16, 6, 16, 6), IsDefault = true };
        save.Click += (_, _) => Save();
        var reset = new Button { Content = "Tout l'image", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        reset.Click += (_, _) => SetSelection(new Rect(0, 0, _source.PixelWidth, _source.PixelHeight));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Children = { reset, save } };
        var row2 = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        row2.Children.Add(buttons);
        row2.Children.Add(_size);

        // Format: free, or a fixed shape that the frame keeps while you drag it.
        var formats = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var formatLabel = new TextBlock { Text = "Format", Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        formats.Children.Add(formatLabel);
        foreach (var (label, ratio) in new (string, double?)[] { ("Libre", null), ("1:1", 1), ("4:3", 4.0 / 3), ("3:4", 3.0 / 4), ("3:2", 3.0 / 2), ("2:3", 2.0 / 3), ("16:9", 16.0 / 9), ("9:16", 9.0 / 16) })
        {
            var pill = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(11, 4, 11, 4), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = new TextBlock { Text = label, FontSize = 12.5 } };
            pill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SetRatio(ratio);
            };
            _ratios.Add((pill, ratio));
            formats.Children.Add(pill);
        }

        var bar = new StackPanel { Margin = new Thickness(14, 10, 14, 10), Children = { formats, row2 } };
        SetRatio(null);

        _image.Source = _source;
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _canvas.Children.Add(_image);
        _canvas.Children.Add(_dim);
        for (var i = 0; i < 4; i++)
        {
            var line = new Line { Stroke = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), StrokeThickness = 1, IsHitTestVisible = false };
            _thirds.Add(line);
            _canvas.Children.Add(line);
        }

        _canvas.Children.Add(_frame);
        foreach (var grip in new[] { Grip.NW, Grip.N, Grip.NE, Grip.E, Grip.SE, Grip.S, Grip.SW, Grip.W })
        {
            var handle = new Rectangle { Width = HandleSize, Height = HandleSize, Fill = Brushes.White, Stroke = new SolidColorBrush(Orange), StrokeThickness = 2, RadiusX = 2, RadiusY = 2, IsHitTestVisible = false };
            _handles[grip] = handle;
            _canvas.Children.Add(handle);
        }

        _canvas.MouseLeftButtonDown += OnDown;
        _canvas.MouseMove += OnMove;
        _canvas.MouseLeftButtonUp += (_, _) =>
        {
            _grip = Grip.None;
            _canvas.ReleaseMouseCapture();
        };
        _canvas.Background = Brushes.Transparent;
        _stage.Child = _canvas;
        var area = new Grid { Children = { _stage } };
        area.SetResourceReference(BackgroundProperty, "Tile.Background");
        area.SizeChanged += (_, e) => Fit(e.NewSize);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(area);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save();
            }
        };
        _selection = new Rect(_source.PixelWidth * 0.1, _source.PixelHeight * 0.1, _source.PixelWidth * 0.8, _source.PixelHeight * 0.8);
        Loaded += (_, _) => Fit(area.RenderSize);
    }

    /// <summary>Decoded and turned upright when the camera stored it sideways (EXIF), as other viewers show it.</summary>
    internal static BitmapSource Load(string path)
    {
        var frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapSource source = frame;
        try
        {
            var angle = frame.Metadata is BitmapMetadata { } metadata && metadata.GetQuery("/app1/ifd/{ushort=274}") is ushort orientation
                ? orientation switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 }
                : 0;
            if (angle != 0)
            {
                source = new TransformedBitmap(frame, new RotateTransform(angle));
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // No readable orientation: keep the pixels as they are.
        }

        source.Freeze();
        return source;
    }

    private void Fit(Size available)
    {
        var width = Math.Max(50, available.Width - 40);
        var height = Math.Max(50, available.Height - 40);
        _scale = Math.Min(width / _source.PixelWidth, height / _source.PixelHeight);
        _canvas.Width = _image.Width = _source.PixelWidth * _scale;
        _canvas.Height = _image.Height = _source.PixelHeight * _scale;
        SetSelection(_selection);
    }

    private void SetSelection(Rect selection)
    {
        var bounds = new Rect(0, 0, _source.PixelWidth, _source.PixelHeight);
        selection.Intersect(bounds);
        _selection = selection.IsEmpty ? bounds : selection;
        var r = new Rect(_selection.X * _scale, _selection.Y * _scale, _selection.Width * _scale, _selection.Height * _scale);

        _dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude,
            new RectangleGeometry(new Rect(0, 0, _canvas.Width, _canvas.Height)), new RectangleGeometry(r));
        _frame.Width = r.Width;
        _frame.Height = r.Height;
        Canvas.SetLeft(_frame, r.X);
        Canvas.SetTop(_frame, r.Y);

        double[] xs = [r.X + r.Width / 3, r.X + r.Width * 2 / 3];
        double[] ys = [r.Y + r.Height / 3, r.Y + r.Height * 2 / 3];
        for (var i = 0; i < 2; i++)
        {
            (_thirds[i].X1, _thirds[i].X2, _thirds[i].Y1, _thirds[i].Y2) = (xs[i], xs[i], r.Y, r.Bottom);
            (_thirds[i + 2].X1, _thirds[i + 2].X2, _thirds[i + 2].Y1, _thirds[i + 2].Y2) = (r.X, r.Right, ys[i], ys[i]);
        }

        foreach (var (grip, handle) in _handles)
        {
            var (x, y) = HandlePoint(grip, r);
            Canvas.SetLeft(handle, x - HandleSize / 2);
            Canvas.SetTop(handle, y - HandleSize / 2);
        }

        _size.Text = $"{_selection.Width:0} × {_selection.Height:0} px   (image : {_source.PixelWidth} × {_source.PixelHeight})";
    }

    private static (double X, double Y) HandlePoint(Grip grip, Rect r) => grip switch
    {
        Grip.NW => (r.Left, r.Top),
        Grip.N => (r.X + r.Width / 2, r.Top),
        Grip.NE => (r.Right, r.Top),
        Grip.E => (r.Right, r.Y + r.Height / 2),
        Grip.SE => (r.Right, r.Bottom),
        Grip.S => (r.X + r.Width / 2, r.Bottom),
        Grip.SW => (r.Left, r.Bottom),
        _ => (r.Left, r.Y + r.Height / 2),
    };

    private Grip HitTest(Point at)
    {
        var r = new Rect(_selection.X * _scale, _selection.Y * _scale, _selection.Width * _scale, _selection.Height * _scale);
        foreach (var grip in _handles.Keys)
        {
            var (x, y) = HandlePoint(grip, r);
            if (Math.Abs(at.X - x) <= HandleSize && Math.Abs(at.Y - y) <= HandleSize)
            {
                return grip;
            }
        }

        // Edges count as handles too, so thin frames can still be grabbed.
        const double Edge = 6;
        var inX = at.X >= r.Left - Edge && at.X <= r.Right + Edge;
        var inY = at.Y >= r.Top - Edge && at.Y <= r.Bottom + Edge;
        if (inY && Math.Abs(at.X - r.Left) <= Edge)
        {
            return Grip.W;
        }

        if (inY && Math.Abs(at.X - r.Right) <= Edge)
        {
            return Grip.E;
        }

        if (inX && Math.Abs(at.Y - r.Top) <= Edge)
        {
            return Grip.N;
        }

        if (inX && Math.Abs(at.Y - r.Bottom) <= Edge)
        {
            return Grip.S;
        }

        return r.Contains(at) ? Grip.Move : Grip.New;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _anchor = e.GetPosition(_canvas);
        _grip = HitTest(_anchor);
        _start = _selection;
        if (_grip == Grip.New)
        {
            _start = new Rect(_anchor.X / _scale, _anchor.Y / _scale, 0, 0);
        }

        _canvas.CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        var at = e.GetPosition(_canvas);
        if (_grip == Grip.None)
        {
            _canvas.Cursor = CursorFor(HitTest(at));
            return;
        }

        var maxX = _source.PixelWidth;
        var maxY = _source.PixelHeight;
        var dx = (at.X - _anchor.X) / _scale;
        var dy = (at.Y - _anchor.Y) / _scale;
        double left = _start.Left, top = _start.Top, right = _start.Right, bottom = _start.Bottom;
        switch (_grip)
        {
            case Grip.Move:
                var width = _start.Width;
                var height = _start.Height;
                left = Math.Clamp(_start.Left + dx, 0, maxX - width);
                top = Math.Clamp(_start.Top + dy, 0, maxY - height);
                right = left + width;
                bottom = top + height;
                break;
            case Grip.New:
                left = Math.Clamp(Math.Min(_start.X, at.X / _scale), 0, maxX);
                right = Math.Clamp(Math.Max(_start.X, at.X / _scale), 0, maxX);
                top = Math.Clamp(Math.Min(_start.Y, at.Y / _scale), 0, maxY);
                bottom = Math.Clamp(Math.Max(_start.Y, at.Y / _scale), 0, maxY);
                break;
            default:
                if (_grip is Grip.W or Grip.NW or Grip.SW)
                {
                    left = Math.Clamp(_start.Left + dx, 0, right - MinSize);
                }

                if (_grip is Grip.E or Grip.NE or Grip.SE)
                {
                    right = Math.Clamp(_start.Right + dx, left + MinSize, maxX);
                }

                if (_grip is Grip.N or Grip.NW or Grip.NE)
                {
                    top = Math.Clamp(_start.Top + dy, 0, bottom - MinSize);
                }

                if (_grip is Grip.S or Grip.SW or Grip.SE)
                {
                    bottom = Math.Clamp(_start.Bottom + dy, top + MinSize, maxY);
                }

                break;
        }

        if (_ratio is { } shape && _grip != Grip.Move)
        {
            (left, top, right, bottom) = KeepRatio(shape, left, top, right, bottom, new Point(at.X / _scale, at.Y / _scale), maxX, maxY);
        }

        SetSelection(new Rect(left, top, right - left, bottom - top));
    }

    private void SetRatio(double? ratio)
    {
        _ratio = ratio;
        foreach (var (pill, value) in _ratios)
        {
            if (value == ratio)
            {
                pill.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
            }
            else
            {
                pill.Background = Brushes.Transparent;
            }
        }

        if (ratio is { } r && _selection.Width > 0)
        {
            // Largest frame of this shape inside the current one, around the same centre.
            var width = Math.Min(_selection.Width, _selection.Height * r);
            var height = width / r;
            var centre = new Point(_selection.X + (_selection.Width / 2), _selection.Y + (_selection.Height / 2));
            SetSelection(new Rect(centre.X - (width / 2), centre.Y - (height / 2), width, height));
        }
    }

    /// <summary>The frame being dragged, brought back to the chosen shape: the opposite corner (or the centre of the opposite edge) stays where it is.</summary>
    private (double Left, double Top, double Right, double Bottom) KeepRatio(
        double ratio, double left, double top, double right, double bottom, Point pointer, double maxX, double maxY)
    {
        switch (_grip)
        {
            case Grip.N or Grip.S:
                var centreX = (_start.Left + _start.Right) / 2;
                var height = bottom - top;
                var width = height * ratio;
                var room = 2 * Math.Min(centreX, maxX - centreX);
                if (width > room)
                {
                    width = room;
                    height = width / ratio;
                }

                return (centreX - (width / 2), _grip == Grip.N ? bottom - height : top, centreX + (width / 2), _grip == Grip.N ? bottom : top + height);
            case Grip.E or Grip.W:
                var centreY = (_start.Top + _start.Bottom) / 2;
                width = right - left;
                height = width / ratio;
                room = 2 * Math.Min(centreY, maxY - centreY);
                if (height > room)
                {
                    height = room;
                    width = height * ratio;
                }

                return (_grip == Grip.W ? right - width : left, centreY - (height / 2), _grip == Grip.W ? right : left + width, centreY + (height / 2));
            default:
                // A new frame, or a corner: one corner is fixed, the pointer decides the size.
                double anchorX, anchorY, pointX, pointY;
                if (_grip == Grip.New)
                {
                    (anchorX, anchorY, pointX, pointY) = (_start.X, _start.Y, pointer.X, pointer.Y);
                }
                else
                {
                    anchorX = _grip is Grip.NW or Grip.SW ? _start.Right : _start.Left;
                    anchorY = _grip is Grip.NW or Grip.NE ? _start.Bottom : _start.Top;
                    pointX = _grip is Grip.NW or Grip.SW ? left : right;
                    pointY = _grip is Grip.NW or Grip.NE ? top : bottom;
                }

                var toRight = pointX >= anchorX;
                var toBottom = pointY >= anchorY;
                width = Math.Min(Math.Abs(pointX - anchorX), Math.Abs(pointY - anchorY) * ratio);
                width = Math.Min(width, Math.Min(toRight ? maxX - anchorX : anchorX, (toBottom ? maxY - anchorY : anchorY) * ratio));
                height = width / ratio;
                return (toRight ? anchorX : anchorX - width, toBottom ? anchorY : anchorY - height, toRight ? anchorX + width : anchorX, toBottom ? anchorY + height : anchorY);
        }
    }

    private static Cursor CursorFor(Grip grip) => grip switch
    {
        Grip.Move => Cursors.SizeAll,
        Grip.N or Grip.S => Cursors.SizeNS,
        Grip.E or Grip.W => Cursors.SizeWE,
        Grip.NW or Grip.SE => Cursors.SizeNWSE,
        Grip.NE or Grip.SW => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    private void Save()
    {
        var area = new Int32Rect((int)Math.Round(_selection.X), (int)Math.Round(_selection.Y), (int)Math.Round(_selection.Width), (int)Math.Round(_selection.Height));
        if (area.Width < 1 || area.Height < 1 || (area.Width == _source.PixelWidth && area.Height == _source.PixelHeight))
        {
            MessageBox.Show(this, "Délimitez d'abord la zone à garder.", "Mandarine", MessageBoxButton.OK, MessageBoxImage.Information);
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
        var output = OutputPaths.ForFile(_path, extension, null, "recadré");
        try
        {
            encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(_source, area)));
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

        TrayBridge.Notify("Image recadrée", $"{System.IO.Path.GetFileName(_path)} → {System.IO.Path.GetFileName(output)} ({area.Width} × {area.Height} px)", output);
        Close();
    }
}
