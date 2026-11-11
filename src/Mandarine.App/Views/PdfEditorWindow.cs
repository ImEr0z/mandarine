using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>
/// Edits the text of a PDF: click a line and type. Lines are text boxes laid over the rendered page; a changed
/// line shows the new text on the page color. "Ajouter du texte" then a click puts new text anywhere.
/// The format bar (font, size, bold, italic, color) acts on the selected line and sets the style of new text.
/// </summary>
internal sealed class PdfEditorWindow : Window
{
    private const double PointsToDip = 96.0 / 72.0;
    private static readonly string[] PreferredFonts =
        ["Arial", "Calibri", "Cambria", "Courier New", "Georgia", "Segoe UI", "Tahoma", "Times New Roman", "Trebuchet MS", "Verdana"];
    private static readonly int[] Palette = [0x000000, 0x1F3A93, 0xC0392B, 0x1E8449, 0xE67E22, 0x7F8C8D];
    private static readonly Color Blue = Color.FromRgb(0x2F, 0x8F, 0xEA);
    private static readonly Color Orange = Color.FromRgb(0xE6, 0x7E, 0x22);

    /// <summary>One line of a page: an original (Original.Text is what the PDF has) or text added by the user.</summary>
    private sealed class Item(PdfTextLine line, bool isNew, int background)
    {
        public PdfTextLine Original { get; } = line;

        /// <summary>Current style and place; its Text is not used, <see cref="Text"/> is.</summary>
        public PdfTextLine Line { get; set; } = line;

        public bool IsNew { get; } = isNew;

        public int Background { get; } = background;

        public string Text { get; set; } = line.Text;

        public bool Changed => IsNew ? Text.Length > 0 : Text != Original.Text || Line != Original;
    }

    private readonly string _path;
    private PdfContent _content;
    private readonly Dictionary<int, List<Item>> _items = [];
    private readonly Canvas _canvas = new();
    private readonly Image _image = new();
    private readonly DispatcherTimer _sharpen = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0), MinWidth = 70, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    private readonly TextBlock _zoomLabel = new() { VerticalAlignment = VerticalAlignment.Center, Width = 44, TextAlignment = TextAlignment.Right, Margin = new Thickness(6, 0, 8, 0) };
    private readonly TextBlock _footer = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _pageSize = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton _addText = (ToggleButton)Tool("", "Ajouter du texte", "Cliquez ensuite sur la page à l'endroit voulu (Échap pour annuler)", toggle: true);
    private readonly ToggleButton _zones = (ToggleButton)Tool("", "Zones", "Montrer toutes les lignes modifiables", toggle: true);
    private readonly ToggleButton _bold = (ToggleButton)Tool("", null, "Gras (Ctrl+B)", toggle: true, compact: true);
    private readonly ToggleButton _italic = (ToggleButton)Tool("", null, "Italique (Ctrl+I)", toggle: true, compact: true);
    private readonly ComboBox _font = new() { Width = 150, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Police" };
    private readonly ComboBox _size = new() { IsEditable = true, Width = 56, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Taille (pt)" };
    private readonly Slider _zoom = new() { Minimum = 0.3, Maximum = 3, Value = 1, Width = 120, VerticalAlignment = VerticalAlignment.Center };
    private readonly ScrollViewer _scroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private BitmapSource? _bitmap;
    private Item? _current;
    private TextBox? _currentBox;
    private int _color;
    private int _renderedDpi;
    private int _page;
    private double _scale;
    private bool _autoFit = true;
    private bool _fitting;
    private bool _syncing;
    private bool _dirty;

    public PdfEditorWindow(string path)
    {
        _path = path;
        _content = PdfEditor.Read(path);
        if (_content.Pages.Count == 0)
        {
            throw new InvalidOperationException("Ce PDF ne contient aucune page.");
        }

        UpdateTitle();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        MinWidth = 860;
        MinHeight = 460;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FitWindowToPage();

        var installed = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var font in PreferredFonts.Where(installed.Contains))
        {
            _font.Items.Add(font);
        }

        foreach (var size in new[] { 8, 9, 10, 11, 12, 14, 16, 18, 24, 36, 48 })
        {
            _size.Items.Add(size);
        }

        _font.SelectedItem = _font.Items.Contains("Arial") ? "Arial" : _font.Items.OfType<string>().FirstOrDefault();
        _size.Text = "12";

        // ---- Handlers of the format bar: they change the selected line, and stay as the style of the next added text.
        _font.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _font.SelectedItem is string font)
            {
                ApplyFormat(l => l with { Font = font });
            }
        };
        _size.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) =>
        {
            if (!_syncing && double.TryParse(_size.Text, out var size) && size is >= 4 and <= 200)
            {
                ApplyFormat(l => l with { FontSize = size });
            }
        }));
        _bold.Click += (_, _) => ApplyFormat(l => l with { Bold = _bold.IsChecked == true });
        _italic.Click += (_, _) => ApplyFormat(l => l with { Italic = _italic.IsChecked == true });
        _zones.Click += (_, _) => RefreshBorders();
        _addText.Checked += (_, _) => _canvas.Cursor = Cursors.IBeam;
        _addText.Unchecked += (_, _) => _canvas.Cursor = null;

        var save = Tool("", "Enregistrer", "Enregistrer dans le PDF (Ctrl+S)", accent: true);
        save.Click += (_, _) => Save();
        var previous = Tool("", null, "Page précédente (Page haut)");
        previous.Click += (_, _) => ShowPage(_page - 1);
        var next = Tool("", null, "Page suivante (Page bas)");
        next.Click += (_, _) => ShowPage(_page + 1);
        var fit = Tool("", "Ajuster", "Adapter la page à la fenêtre (Ctrl+0)");
        fit.Click += (_, _) => FitToViewport();
        _zoom.ValueChanged += (_, _) =>
        {
            if (!_fitting)
            {
                _autoFit = false;
            }

            Relayout();
            // While the zoom moves the current bitmap is stretched; once it settles, render again at the right resolution.
            _sharpen.Stop();
            _sharpen.Start();
        };
        _sharpen.Tick += (_, _) =>
        {
            _sharpen.Stop();
            // Any difference: a bitmap stretched even by 2 % is visibly soft.
            if (_bitmap is not null && Math.Abs(WantedDpi() - _renderedDpi) > 1 && Render())
            {
                Relayout();
            }
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        // ---- Row 1: file, pages, add text, zoom.
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (UIElement element in new UIElement[] { save, Separator(), previous, _pageLabel, next, Separator(), _addText, _zones })
        {
            left.Children.Add(element);
        }

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (UIElement element in new UIElement[] { _status, _zoom, _zoomLabel, fit })
        {
            right.Children.Add(element);
        }

        var row1 = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(right, Dock.Right);
        row1.Children.Add(right);
        row1.Children.Add(left);

        // ---- Row 2: format of the selected line.
        var row2 = new StackPanel { Orientation = Orientation.Horizontal };
        row2.Children.Add(new TextBlock { Text = "Texte", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0), Opacity = 0.65 });
        foreach (UIElement element in new UIElement[] { _font, _size, _bold, _italic, Separator() })
        {
            row2.Children.Add(element);
        }

        foreach (var rgb in Palette)
        {
            row2.Children.Add(Swatch(rgb));
        }

        var bar = new Border { Padding = new Thickness(12, 8, 12, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = new StackPanel { Children = { row1, row2 } } };
        bar.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        // ---- Status bar.
        var statusBar = new DockPanel { Margin = new Thickness(14, 5, 14, 5) };
        DockPanel.SetDock(_pageSize, Dock.Right);
        statusBar.Children.Add(_pageSize);
        statusBar.Children.Add(_footer);
        var footer = new Border { Child = statusBar, BorderThickness = new Thickness(0, 1, 0, 0), Opacity = 0.9 };
        footer.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        _footer.SetResourceReference(ForegroundProperty, "Text.Secondary");
        _pageSize.SetResourceReference(ForegroundProperty, "Text.Secondary");

        // ---- Page.
        _canvas.Children.Add(_image);
        _canvas.MouseLeftButtonDown += OnCanvasClick;
        var page = new Border { Child = _canvas, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24), Background = Brushes.White };
        // No drop shadow effect: it renders the page through an intermediate surface and softens it. A thin outline instead.
        page.BorderThickness = new Thickness(1);
        page.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        _scroll.Content = page;
        _scroll.SetResourceReference(BackgroundProperty, "Tile.Background");
        _scroll.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                ZoomBy(e.Delta > 0 ? 1.1 : 1 / 1.1);
                e.Handled = true;
            }
        };

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(bar);
        root.Children.Add(footer);
        root.Children.Add(_scroll);
        Content = root;

        AddKey(Key.S, ModifierKeys.Control, () => Save());
        AddKey(Key.D0, ModifierKeys.Control, FitToViewport);
        AddKey(Key.OemPlus, ModifierKeys.Control, () => ZoomBy(1.1));
        AddKey(Key.Add, ModifierKeys.Control, () => ZoomBy(1.1));
        AddKey(Key.OemMinus, ModifierKeys.Control, () => ZoomBy(1 / 1.1));
        AddKey(Key.Subtract, ModifierKeys.Control, () => ZoomBy(1 / 1.1));
        AddKey(Key.B, ModifierKeys.Control, () => { _bold.IsChecked = _bold.IsChecked != true; ApplyFormat(l => l with { Bold = _bold.IsChecked == true }); });
        AddKey(Key.I, ModifierKeys.Control, () => { _italic.IsChecked = _italic.IsChecked != true; ApplyFormat(l => l with { Italic = _italic.IsChecked == true }); });
        AddKey(Key.PageUp, ModifierKeys.None, () => ShowPage(_page - 1));
        AddKey(Key.PageDown, ModifierKeys.None, () => ShowPage(_page + 1));
        AddKey(Key.Escape, ModifierKeys.None, () =>
        {
            if (_addText.IsChecked == true)
            {
                _addText.IsChecked = false;
            }
            else
            {
                Keyboard.ClearFocus();
            }
        });

        Closing += OnClosing;
        _scroll.SizeChanged += (_, _) =>
        {
            if (_autoFit)
            {
                FitToViewport();
            }
        };
        Loaded += (_, _) =>
        {
            ShowPage(0);
            Dispatcher.BeginInvoke(FitToViewport, DispatcherPriority.Loaded);
            Dispatcher.BeginInvoke(FitToViewport, DispatcherPriority.ApplicationIdle);
        };
    }

    private void AddKey(Key key, ModifierKeys modifiers, Action action) =>
        InputBindings.Add(new KeyBinding(new RelayCommand(action), key, modifiers));

    private void UpdateTitle() => Title = $"Modifier le texte – {Path.GetFileName(_path)}{(_dirty ? " •" : string.Empty)}";

    private void UpdateFooter()
    {
        var changes = _items.Values.SelectMany(i => i).Count(i => i.Changed);
        var hint = changes == 0 ? "Cliquez sur une ligne pour la modifier." : $"{changes} ligne{(changes > 1 ? "s" : string.Empty)} modifiée{(changes > 1 ? "s" : string.Empty)}, non enregistrée{(changes > 1 ? "s" : string.Empty)}.";
        _footer.Text = hint;
        var (width, height) = _content.Pages[_page];
        _pageSize.Text = $"{width * 25.4 / 72:0} × {height * 25.4 / 72:0} mm";
    }

    private void ZoomBy(double factor) => _zoom.Value = Math.Clamp(_zoom.Value * factor, _zoom.Minimum, _zoom.Maximum);

    /// <summary>Opens the window just big enough for the whole first page (zoom reduced if the screen is too small).</summary>
    private void FitWindowToPage()
    {
        const double ChromeWidth = 34, ChromeHeight = 165;
        var work = SystemParameters.WorkArea;
        _zoom.Value = Math.Min(1.5, FitZoom(work.Width * 0.95 - ChromeWidth, work.Height * 0.95 - ChromeHeight));
        var (width, height) = _content.Pages[0];
        Width = Math.Max(MinWidth, width * PointsToDip * _zoom.Value + 48 + ChromeWidth);
        Height = Math.Max(MinHeight, height * PointsToDip * _zoom.Value + 48 + ChromeHeight);
    }

    private double FitZoom(double availableWidth, double availableHeight)
    {
        var (width, height) = _content.Pages[_page];
        return Math.Clamp(Math.Min((availableWidth - 48) / (width * PointsToDip), (availableHeight - 48) / (height * PointsToDip)), 0.3, 3);
    }

    private void FitToViewport()
    {
        // Fitting stays on (the zoom follows the window size) until the user zooms by hand.
        // Right after opening, the viewport is not measured yet: use the size of the scroll area itself.
        var width = _scroll.ViewportWidth > 0 ? _scroll.ViewportWidth : _scroll.ActualWidth;
        var height = _scroll.ViewportHeight > 0 ? _scroll.ViewportHeight : _scroll.ActualHeight;
        if (width > 0 && height > 0)
        {
            _fitting = true;
            _zoom.Value = FitZoom(width, height);
            _fitting = false;
            _autoFit = true;
        }
    }

    // ---- Toolbar pieces ----

    private static Border Separator()
    {
        var line = new Border { Width = 1, Height = 20, Margin = new Thickness(8, 0, 8, 0) };
        line.SetResourceReference(Border.BackgroundProperty, "Panel.Border");
        return line;
    }

    private Button Swatch(int rgb)
    {
        var button = (Button)Tool(string.Empty, null, "Couleur du texte", compact: true);
        var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(FromRgb(rgb)), BorderThickness = new Thickness(1) };
        dot.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        button.Content = dot;
        button.Click += (_, _) =>
        {
            _color = rgb;
            ApplyFormat(l => l with { Color = rgb });
        };
        return button;
    }

    /// <summary>Flat toolbar button: Segoe icon, optional label. Accent fill for the main action.</summary>
    private static ButtonBase Tool(string glyph, string? label, string tip, bool accent = false, bool toggle = false, bool compact = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        content.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (label is not null)
        {
            content.Children.Add(new TextBlock { Text = label, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        ButtonBase button = toggle ? new ToggleButton() : new Button();
        button.Content = content;
        button.ToolTip = tip;
        button.Margin = new Thickness(2, 0, 2, 0);
        button.Cursor = Cursors.Hand;
        button.Focusable = false;
        button.Template = ToolTemplate(accent, toggle, compact);
        if (accent)
        {
            button.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
        }
        else
        {
            button.SetResourceReference(ForegroundProperty, "Text.Primary");
        }

        return button;
    }

    private static ControlTemplate ToolTemplate(bool accent, bool toggle, bool compact)
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, compact ? new Thickness(7, 5, 7, 5) : new Thickness(10, 6, 10, 6));
        if (accent)
        {
            border.SetResourceReference(Border.BackgroundProperty, "Accent");
        }
        else
        {
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        }

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(ButtonBase)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(accent ? "Accent.Strong" : "Tile.Hover"), "bd"));
        var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Tile.Pressed"), "bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(pressed);
        if (toggle)
        {
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Accent.Soft"), "bd"));
            template.Triggers.Add(on);
        }

        return template;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        Keyboard.ClearFocus();
        if (!_dirty)
        {
            return;
        }

        switch (MessageBox.Show(this, "Enregistrer les modifications dans le PDF ?", "Mandarine", MessageBoxButton.YesNoCancel, MessageBoxImage.Question))
        {
            case MessageBoxResult.Yes:
                Save(close: false);
                e.Cancel = _dirty; // saving failed: stay open
                break;
            case MessageBoxResult.No:
                break;
            default:
                e.Cancel = true;
                break;
        }
    }

    // ---- Pages ----

    private void ShowPage(int page)
    {
        if (page < 0 || page >= _content.Pages.Count)
        {
            return;
        }

        _page = page;
        if (!Render())
        {
            return;
        }

        if (!_items.ContainsKey(page))
        {
            _items[page] = _content.Lines.Where(l => l.Page == page).Select(l => new Item(l, false, SampleBackground(l))).ToList();
        }

        _current = null;
        _currentBox = null;
        _pageLabel.Text = $"Page {page + 1} / {_content.Pages.Count}";
        Relayout();
        UpdateFooter();
        if (_autoFit)
        {
            FitToViewport();
        }
    }

    /// <summary>One bitmap pixel per screen pixel at the current zoom: sharp, never a stretched low-resolution picture.</summary>
    private int WantedDpi() => (int)Math.Clamp(96 * _zoom.Value * VisualTreeHelper.GetDpi(this).DpiScaleX, 72, 400);

    private bool Render()
    {
        try
        {
            var dpi = WantedDpi();
            using var stream = new MemoryStream(Raster.PdfPageToPng(_path, _page, dpi));
            var decoded = new BitmapImage();
            decoded.BeginInit();
            decoded.CacheOption = BitmapCacheOption.OnLoad;
            decoded.StreamSource = stream;
            decoded.EndInit();
            decoded.Freeze();
            _bitmap = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);
            _bitmap.Freeze();
            _renderedDpi = dpi;
            _image.Source = _bitmap;
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Impossible d'afficher cette page : " + ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    /// <summary>Page color just left of the line, so that an edit hides the old text on colored backgrounds too.</summary>
    private int SampleBackground(PdfTextLine line)
    {
        if (_bitmap is null)
        {
            return 0xFFFFFF;
        }

        var pixelsPerPoint = _renderedDpi / 72.0;
        var x = Math.Clamp((int)((line.X - 2) * pixelsPerPoint), 0, _bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)((_content.Pages[_page].Height - line.Y - line.Height / 2) * pixelsPerPoint), 0, _bitmap.PixelHeight - 1);
        var pixel = new byte[4];
        _bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return (pixel[2] << 16) | (pixel[1] << 8) | pixel[0];
    }

    /// <summary>Draws the current page and its boxes again at the current zoom.</summary>
    private void Relayout()
    {
        if (_bitmap is null)
        {
            return;
        }

        Keyboard.ClearFocus();
        var (width, height) = _content.Pages[_page];
        _scale = PointsToDip * _zoom.Value;
        if (Math.Abs(WantedDpi() - _renderedDpi) <= 1)
        {
            // Bitmap at the right resolution: show it pixel for pixel (a fractional size would be resampled and look soft).
            _scale = _bitmap.PixelWidth / VisualTreeHelper.GetDpi(this).DpiScaleX / width;
        }

        _zoomLabel.Text = _zoom.Value.ToString("P0");
        _canvas.Width = width * _scale;
        _canvas.Height = height * _scale;
        _image.Width = _canvas.Width;
        _image.Height = _canvas.Height;
        for (var i = _canvas.Children.Count - 1; i >= 0; i--)
        {
            if (_canvas.Children[i] is TextBox)
            {
                _canvas.Children.RemoveAt(i);
            }
        }

        _currentBox = null;
        foreach (var item in _items[_page])
        {
            if (!item.IsNew || item.Changed)
            {
                var box = AddBox(item);
                if (item == _current)
                {
                    _currentBox = box;
                }
            }
        }
    }

    // ---- Text boxes ----

    private TextBox AddBox(Item item)
    {
        var box = new TextBox
        {
            Text = item.Text,
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = item,
            Template = BoxTemplate(),
            BorderBrush = Brushes.Transparent,
        };
        ApplyBoxStyle(box, item);
        box.TextChanged += (_, _) =>
        {
            item.Text = box.Text;
            MarkDirty();
            ApplyLook(box, item);
        };
        box.GotKeyboardFocus += (_, _) => SelectItem(item, box);
        box.LostKeyboardFocus += (_, _) =>
        {
            UpdateBorder(box);
            if (item.IsNew && box.Text.Length == 0)
            {
                _canvas.Children.Remove(box);
            }
        };
        box.MouseEnter += (_, _) => UpdateBorder(box);
        box.MouseLeave += (_, _) => UpdateBorder(box);
        box.ContextMenu = BoxMenu(item, box);
        _canvas.Children.Add(box);
        return box;
    }

    private ContextMenu BoxMenu(Item item, TextBox box)
    {
        var restore = new MenuItem { Header = "Rétablir le texte d'origine" };
        restore.Click += (_, _) =>
        {
            item.Line = item.Original;
            box.Text = item.Original.Text;
            ApplyBoxStyle(box, item);
            ApplyLook(box, item);
            if (item == _current)
            {
                SelectItem(item, box);
            }
        };
        var delete = new MenuItem { Header = "Supprimer la ligne" };
        delete.Click += (_, _) =>
        {
            box.Text = string.Empty;
            if (item.IsNew)
            {
                _canvas.Children.Remove(box);
            }
        };
        var menu = new ContextMenu();
        menu.Opened += (_, _) => restore.IsEnabled = !item.IsNew && item.Changed;
        if (!item.IsNew)
        {
            menu.Items.Add(restore);
        }

        menu.Items.Add(delete);
        return menu;
    }

    private void ApplyBoxStyle(TextBox box, Item item)
    {
        var line = item.Line;
        var height = Math.Max(line.Height, line.FontSize * 1.2);
        box.MinWidth = line.Width * _scale;
        box.Height = height * _scale;
        box.FontFamily = new FontFamily(line.Font);
        box.FontSize = line.FontSize * _scale;
        box.FontWeight = line.Bold ? FontWeights.Bold : FontWeights.Normal;
        box.FontStyle = line.Italic ? FontStyles.Italic : FontStyles.Normal;
        Canvas.SetLeft(box, line.X * _scale);
        Canvas.SetTop(box, (_content.Pages[line.Page].Height - line.Y - height) * _scale);
        ApplyLook(box, item);
    }

    /// <summary>Untouched: invisible text over the rendered one. Changed: new text on the page color.</summary>
    private void ApplyLook(TextBox box, Item item)
    {
        if (item.Changed)
        {
            box.Foreground = new SolidColorBrush(FromRgb(item.Line.Color));
            box.Background = new SolidColorBrush(item.IsNew ? Colors.Transparent : FromRgb(item.Background));
            box.CaretBrush = new SolidColorBrush(FromRgb(item.Line.Color));
        }
        else
        {
            box.Foreground = Brushes.Transparent;
            box.Background = Brushes.Transparent;
            box.CaretBrush = Brushes.Black;
        }

        UpdateBorder(box);
    }

    /// <summary>Outline: strong when typing, medium under the mouse, orange when changed, faint when "Zones" is on.</summary>
    private void UpdateBorder(TextBox box)
    {
        var item = (Item)box.Tag;
        Brush brush = Brushes.Transparent;
        if (box.IsKeyboardFocusWithin)
        {
            brush = new SolidColorBrush(Blue);
        }
        else if (box.IsMouseOver)
        {
            brush = new SolidColorBrush(Blue) { Opacity = 0.7 };
        }
        else if (item.Changed)
        {
            brush = new SolidColorBrush(Orange) { Opacity = 0.8 };
        }
        else if (_zones.IsChecked == true)
        {
            brush = new SolidColorBrush(Blue) { Opacity = 0.35 };
        }

        box.BorderBrush = brush;
    }

    private void RefreshBorders()
    {
        foreach (var box in _canvas.Children.OfType<TextBox>())
        {
            UpdateBorder(box);
        }
    }

    private static Color FromRgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static ControlTemplate BoxTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var host = new FrameworkElementFactory(typeof(ScrollViewer));
        host.Name = "PART_ContentHost";
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    // ---- Selection and format ----

    private void MarkDirty()
    {
        _dirty = true;
        _status.Text = string.Empty;
        UpdateTitle();
        UpdateFooter();
    }

    /// <summary>The format bar shows the style of the line being edited.</summary>
    private void SelectItem(Item item, TextBox box)
    {
        _current = item;
        _currentBox = box;
        _syncing = true;
        var line = item.Line;
        if (!_font.Items.Contains(line.Font))
        {
            _font.Items.Add(line.Font);
        }

        _font.SelectedItem = line.Font;
        _size.Text = line.FontSize.ToString("0.#");
        _bold.IsChecked = line.Bold;
        _italic.IsChecked = line.Italic;
        _color = line.Color;
        _syncing = false;
        UpdateBorder(box);
    }

    private void ApplyFormat(Func<PdfTextLine, PdfTextLine> change)
    {
        if (_current is null || _currentBox is null)
        {
            return; // nothing selected: the bar only sets the style of the next added text
        }

        _current.Line = change(_current.Line);
        ApplyBoxStyle(_currentBox, _current);
        MarkDirty();
    }

    private void OnCanvasClick(object sender, MouseButtonEventArgs e)
    {
        if (_addText.IsChecked != true || e.OriginalSource is TextBox)
        {
            return;
        }

        var size = double.TryParse(_size.Text, out var parsed) ? Math.Clamp(parsed, 4, 200) : 12;
        var point = e.GetPosition(_canvas);
        var pageHeight = _content.Pages[_page].Height;
        var x = point.X / _scale;
        var baseline = pageHeight - point.Y / _scale + size * 0.35;
        var line = new PdfTextLine(_page, string.Empty, x, baseline - size * 0.25, size * 4, size * 1.2, baseline, size,
            _font.SelectedItem as string ?? "Arial", _bold.IsChecked == true, _italic.IsChecked == true, _color);
        var item = new Item(line, true, 0xFFFFFF);
        _items[_page].Add(item);
        AddBox(item).Focus();
        _addText.IsChecked = false;
        e.Handled = true;
    }

    /// <summary>Writes into the open PDF, then closes the window (unless it is already closing).</summary>
    private void Save(bool close = true)
    {
        Keyboard.ClearFocus();
        var edits = _items.Values.SelectMany(i => i).Where(i => i.Changed)
            .Select(i => new PdfEdit(i.IsNew ? null : i.Original, i.Line with { Text = i.Text }, i.Background)).ToList();
        if (edits.Count == 0)
        {
            _dirty = false;
            UpdateTitle();
            _status.Text = "Aucune modification à enregistrer.";
            return;
        }

        try
        {
            PdfEditor.Save(_path, _path, edits);
            _dirty = false;
            if (close)
            {
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

file sealed class RelayCommand(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => action();
}
