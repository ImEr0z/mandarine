using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Mandarine.App.Services;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Light, colour and angle of a picture, with a live preview. Straightens a tilted photo or scan.</summary>
internal sealed class ImageAdjustWindow : Window
{
    private readonly string _path;
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly Canvas _grid = new() { IsHitTestVisible = false };
    private readonly Grid _stage = new() { Margin = new Thickness(20) };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private readonly Dictionary<string, Slider> _sliders = [];
    private readonly CheckBox _crop = new() { IsChecked = true };
    private readonly CheckBox _showGrid = new() { IsChecked = true };
    private readonly TextBlock _problem = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B)), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
    private int _generation;
    private readonly Lazy<ImageMagick.MagickImage> _preview;

    public ImageAdjustWindow(string path)
    {
        _path = path;
        _preview = new(() => ImageAdjust.LoadPreviewBase(path, 1400));
        Title = $"Ajuster – {System.IO.Path.GetFileName(path)}";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var work = SystemParameters.WorkArea;
        Width = Math.Min(1180, work.Width * 0.9);
        Height = Math.Min(760, work.Height * 0.9);
        MinWidth = 820;
        MinHeight = 520;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");
        UseLayoutRounding = true;

        // ---- Controls
        var panel = new StackPanel { Margin = new Thickness(18, 16, 18, 16) };
        panel.Children.Add(new TextBlock { Text = "Lumière et couleurs", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var (key, label, min, max) in new[] { ("b", "Luminosité", -100.0, 100.0), ("c", "Contraste", -100.0, 100.0), ("s", "Saturation", -100.0, 100.0), ("w", "Chaleur", -100.0, 100.0) })
        {
            panel.Children.Add(SliderRow(key, label, min, max, 1));
        }

        var angleTitle = new TextBlock { Text = "Redresser", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 6) };
        panel.Children.Add(angleTitle);
        panel.Children.Add(SliderRow("a", "Angle (°)", -45, 45, 0.1));
        _crop.Content = new TextBlock { Text = "Rogner les coins vides après rotation", TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
        _crop.Margin = new Thickness(0, 6, 0, 2);
        _showGrid.Content = new TextBlock { Text = "Afficher la grille d'alignement", FontSize = 13.5 };
        _showGrid.Margin = new Thickness(0, 6, 0, 0);
        _crop.Click += (_, _) => Schedule();
        _showGrid.Click += (_, _) => DrawGrid();
        panel.Children.Add(_crop);
        panel.Children.Add(_showGrid);
        panel.Children.Add(_problem);

        var reset = DialogStyle.Button("Réinitialiser", primary: false);
        reset.Margin = new Thickness(0, 0, 8, 0);
        reset.Click += (_, _) =>
        {
            foreach (var slider in _sliders.Values)
            {
                slider.Value = 0;
            }
        };
        var save = DialogStyle.Button("Enregistrer une copie", primary: true, isDefault: true);
        save.Margin = new Thickness(0);
        save.Click += async (_, _) => await SaveAsync();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 10, 14, 16), Children = { reset, save } };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var side = new DockPanel { Width = 350, Children = { buttons, new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } };
        var sideFrame = new Border { Child = side, BorderThickness = new Thickness(1, 0, 0, 0) };
        sideFrame.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        DockPanel.SetDock(sideFrame, Dock.Right);

        // ---- Preview with an alignment grid on top
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        _stage.Children.Add(_image);
        _stage.Children.Add(_grid);
        var area = new Grid { Children = { _stage } };
        area.SetResourceReference(BackgroundProperty, "Tile.Background");
        _image.SizeChanged += (_, _) => DrawGrid();

        var root = new DockPanel { Children = { sideFrame, area } };
        Content = root;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
            }
        };
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) =>
        {
            if (_preview.IsValueCreated)
            {
                _preview.Value.Dispose();
            }
        };
    }

    private UIElement SliderRow(string key, string label, double min, double max, double step)
    {
        var value = new TextBlock { Text = "0", Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right };
        var slider = new Slider { Minimum = min, Maximum = max, Value = 0, TickFrequency = step, IsSnapToTickEnabled = step < 1, SmallChange = step };
        slider.ValueChanged += (_, _) =>
        {
            value.Text = step < 1 ? $"{slider.Value:0.0}" : $"{slider.Value:0}";
            Schedule();
        };
        slider.MouseDoubleClick += (_, _) => slider.Value = 0; // double click: back to neutral
        _sliders[key] = slider;
        var head = new DockPanel { Children = { value, new TextBlock { Text = label } } };
        DockPanel.SetDock(value, Dock.Right);
        return new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { head, slider } };
    }

    private AdjustSettings Settings => new(_sliders["b"].Value, _sliders["c"].Value, _sliders["s"].Value, _sliders["w"].Value, _sliders["a"].Value, _crop.IsChecked == true);

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RefreshAsync()
    {
        var generation = ++_generation;
        var settings = Settings;
        try
        {
            var bitmap = await Task.Run(() =>
            {
                // Decoded and shrunk once: moving a slider only adjusts a copy of the small picture.
                using var stream = new MemoryStream(ImageAdjust.Preview(_preview.Value, settings));
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            });
            if (generation == _generation)
            {
                _image.Source = bitmap;
                _problem.Text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _problem.Text = ex.Message;
            }
        }
    }

    /// <summary>Thin lines over the preview, to line a horizon or a page edge up with.</summary>
    private void DrawGrid()
    {
        _grid.Children.Clear();
        if (_showGrid.IsChecked != true || _image.ActualWidth < 10)
        {
            return;
        }

        _grid.Width = _image.ActualWidth;
        _grid.Height = _image.ActualHeight;
        _grid.HorizontalAlignment = HorizontalAlignment.Center;
        _grid.VerticalAlignment = VerticalAlignment.Center;
        var brush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        for (var i = 1; i < 10; i++)
        {
            _grid.Children.Add(new Line { X1 = _grid.Width * i / 10, X2 = _grid.Width * i / 10, Y1 = 0, Y2 = _grid.Height, Stroke = brush, StrokeThickness = 1 });
            _grid.Children.Add(new Line { Y1 = _grid.Height * i / 10, Y2 = _grid.Height * i / 10, X1 = 0, X2 = _grid.Width, Stroke = brush, StrokeThickness = 1 });
        }
    }

    private async Task SaveAsync()
    {
        var settings = Settings;
        if (settings.IsNeutral)
        {
            MessageBox.Show(this, "Réglez d'abord quelque chose (lumière, couleurs ou angle).", "Mandarine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        IsEnabled = false;
        var extension = System.IO.Path.GetExtension(_path).TrimStart('.');
        var onlyAngle = settings is { Brightness: 0, Contrast: 0, Saturation: 0, Warmth: 0 };
        var output = OutputPaths.ForFile(_path, extension, null, onlyAngle ? "redressé" : "retouché");
        try
        {
            await Task.Run(() => ImageAdjust.Save(_path, output, settings));
        }
        catch (Exception ex)
        {
            OutputPaths.Discard(output);
            ErrorLog.Write(ex);
            IsEnabled = true;
            _problem.Text = ex.Message;
            return;
        }

        TrayBridge.Notify(onlyAngle ? "Image redressée" : "Image retouchée",
            $"{System.IO.Path.GetFileName(_path)} → {System.IO.Path.GetFileName(output)}", output);
        Close();
    }
}
