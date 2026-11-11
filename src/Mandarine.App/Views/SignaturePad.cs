using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Mandarine.App.Views;

/// <summary>Draw a signature with the mouse, pen or finger. Returns a PNG with a transparent background, cropped to the ink.</summary>
internal sealed class SignaturePad : Window
{
    private readonly InkCanvas _ink = new()
    {
        Height = 180,
        Background = Brushes.Transparent,
        DefaultDrawingAttributes = new DrawingAttributes { Color = Colors.Black, Width = 2.6, Height = 2.6, FitToCurve = true },
    };

    private readonly TextBox _name = new() { Text = "Ma signature", Width = 170 };
    private readonly CheckBox _keep = new() { IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
    private byte[]? _png;
    private readonly Stack<Stroke> _undone = new();
    private bool _restoring;

    private SignaturePad(Window? owner)
    {
        Title = "Signature";
        Owner = owner;
        DialogStyle.Frameless(this, 540);
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        DialogStyle.StyleField(_name, 34);

        // ---- Drawing area: always a white sheet, as the signature is placed on paper.
        var baseline = new Border { Height = 1, Background = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xDD)), Margin = new Thickness(30, 0, 30, 38), VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
        var hint = new TextBlock { Text = "Signez ici avec la souris, le stylet ou le doigt", FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA8)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var sheet = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
            Child = new Grid { Children = { hint, baseline, _ink } },
        };
        sheet.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");

        // ---- Colour and thickness: changing them also changes what is already drawn.
        var swatches = new List<Border>();
        void Pick(Color color, Border chosen)
        {
            _ink.DefaultDrawingAttributes.Color = color;
            foreach (var stroke in _ink.Strokes)
            {
                stroke.DrawingAttributes.Color = color;
            }

            foreach (var swatch in swatches)
            {
                if (swatch == chosen)
                {
                    swatch.SetResourceReference(Border.BorderBrushProperty, "Accent");
                }
                else
                {
                    swatch.BorderBrush = Brushes.Transparent;
                }
            }
        }

        var colors = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var color in new[] { Colors.Black, Color.FromRgb(0x1F, 0x3A, 0x93), Color.FromRgb(0x2F, 0x8F, 0xEA), Color.FromRgb(0xC0, 0x39, 0x2B), Color.FromRgb(0x1E, 0x84, 0x49), Color.FromRgb(0x7D, 0x3C, 0x98) })
        {
            var dot = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(color) };
            var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 0), BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = dot, ToolTip = "Couleur" };
            swatch.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true; // not the start of a window drag
                Pick(color, swatch);
            };
            swatches.Add(swatch);
            colors.Children.Add(swatch);
        }

        swatches[0].SetResourceReference(Border.BorderBrushProperty, "Accent");

        var pills = new List<Border>();
        var pillRow = new StackPanel { Orientation = Orientation.Horizontal };
        var thickness = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(2), VerticalAlignment = VerticalAlignment.Center, Child = pillRow };
        thickness.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        void Select(Border pill)
        {
            foreach (var other in pills)
            {
                if (other == pill)
                {
                    other.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
                }
                else
                {
                    other.Background = Brushes.Transparent;
                }
            }
        }

        foreach (var (label, size) in new[] { ("Fin", 1.6), ("Moyen", 2.6), ("Épais", 4.5) })
        {
            var pill = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 4, 12, 4), Cursor = Cursors.Hand, Child = new TextBlock { Text = label, FontSize = 12.5 } };
            pill.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                _ink.DefaultDrawingAttributes.Width = _ink.DefaultDrawingAttributes.Height = size;
                foreach (var stroke in _ink.Strokes)
                {
                    stroke.DrawingAttributes.Width = stroke.DrawingAttributes.Height = size;
                }

                Select(pill);
            };
            pills.Add(pill);
            pillRow.Children.Add(pill);
        }

        Select(pills[1]);
        var options = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(thickness, Dock.Right);
        options.Children.Add(thickness);
        options.Children.Add(colors);

        // ---- Keep for next time
        var keepText = new TextBlock { Text = "Garder pour la prochaine fois", FontSize = 13.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 12, 0), Cursor = Cursors.Hand };
        keepText.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            _keep.IsChecked = _keep.IsChecked != true;
        };
        var nameLabel = DialogStyle.Muted("Nom", 13);
        nameLabel.VerticalAlignment = VerticalAlignment.Center;
        nameLabel.Margin = new Thickness(0, 0, 8, 0);
        var nameBox = new StackPanel { Orientation = Orientation.Horizontal, Children = { nameLabel, _name } };
        var keepRow = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(nameBox, Dock.Right);
        keepRow.Children.Add(nameBox);
        keepRow.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { _keep, keepText } });
        _keep.Checked += (_, _) => nameBox.IsEnabled = true;
        _keep.Unchecked += (_, _) => nameBox.IsEnabled = false;

        // ---- Buttons
        var clear = DialogStyle.Button("Effacer", primary: false);
        var cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        var ok = DialogStyle.Button("Utiliser", primary: true, isDefault: true);
        ok.IsEnabled = false;
        clear.Click += (_, _) =>
        {
            _ink.Strokes.Clear();
            _undone.Clear();
        };
        // Ctrl+Z removes the last stroke, Ctrl+Y (or Ctrl+Shift+Z) brings it back. In the name field, they edit the text.
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.FocusedElement is not TextBox)
            {
                var redo = e.Key == Key.Y || (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                if (e.Key == Key.Z && !redo)
                {
                    Undo();
                    e.Handled = true;
                }
                else if (redo)
                {
                    Redo();
                    e.Handled = true;
                }
            }
        };
        ok.Click += (_, _) =>
        {
            // Rendered while the window is still open: its size is known.
            _png = ToPng();
            DialogResult = _png is not null;
        };
        _ink.Strokes.StrokesChanged += (_, e) =>
        {
            if (!_restoring && e.Added.Count > 0)
            {
                _undone.Clear(); // a new stroke ends the redo history
            }

            ok.IsEnabled = _ink.Strokes.Count > 0;
            hint.Visibility = _ink.Strokes.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0), Children = { clear, cancel, ok } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children = { DialogStyle.Header("", "Signature", "Dessinez votre signature", () => DialogResult = false), sheet, options, keepRow, actions },
        });
        Content = card;
        DialogStyle.Appear(this, card);
    }

    private void Undo()
    {
        if (_ink.Strokes.Count > 0)
        {
            _restoring = true;
            _undone.Push(_ink.Strokes[^1]);
            _ink.Strokes.RemoveAt(_ink.Strokes.Count - 1);
            _restoring = false;
        }
    }

    private void Redo()
    {
        if (_undone.Count > 0)
        {
            _restoring = true;
            _ink.Strokes.Add(_undone.Pop());
            _restoring = false;
        }
    }

    /// <summary>The drawn signature, and the name to keep it under (null: use it once).</summary>
    public sealed record Result(byte[] Png, string? SaveAs);

    /// <summary>Null when cancelled.</summary>
    public static Result? Draw(Window? owner)
    {
        var pad = new SignaturePad(owner);
        if (pad.ShowDialog() != true || pad._png is not { } png)
        {
            return null;
        }

        return new Result(png, pad._keep.IsChecked == true ? pad._name.Text : null);
    }

    private byte[]? ToPng()
    {
        var width = (int)_ink.ActualWidth;
        var height = (int)_ink.ActualHeight;
        var bounds = _ink.Strokes.GetBounds();
        if (width < 1 || height < 1 || bounds.IsEmpty)
        {
            return null;
        }

        var surface = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        surface.Render(_ink);
        var x = Math.Clamp((int)bounds.X - 6, 0, width - 1);
        var y = Math.Clamp((int)bounds.Y - 6, 0, height - 1);
        var cropped = new CroppedBitmap(surface, new Int32Rect(x, y, Math.Min(width - x, (int)bounds.Width + 12), Math.Min(height - y, (int)bounds.Height + 12)));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(cropped));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
