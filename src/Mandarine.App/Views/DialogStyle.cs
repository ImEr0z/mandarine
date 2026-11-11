using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Mandarine.App.Views;

/// <summary>
/// Shared look of Mandarine's small dialogs: neutral surfaces that follow the app theme (light, dark, high contrast),
/// with the mandarine orange kept for the accent only (icon, focus, main button, selection).
/// </summary>
internal static class DialogStyle
{
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    /// <summary>Window with no frame, transparent, to host a <see cref="Card"/>.</summary>
    public static void Frameless(Window window, double width)
    {
        window.Width = width;
        window.SizeToContent = SizeToContent.Height;
        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = true;
        window.Background = Brushes.Transparent;
        window.ResizeMode = ResizeMode.NoResize;
        window.SetResourceReference(Control.FontFamilyProperty, "TextFont");
        window.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
    }

    public static Border Card(UIElement content)
    {
        var card = new Border
        {
            Margin = new Thickness(20),
            Padding = new Thickness(22),
            CornerRadius = new CornerRadius(14),
            BorderThickness = new Thickness(1),
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Opacity = .35, Color = Colors.Black },
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.97, 0.97),
            Opacity = 0,
            Child = content,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Panel.Background");
        card.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        return card;
    }

    /// <summary>Short fade and zoom when the dialog appears; the window can be dragged by its card.</summary>
    public static void Appear(Window window, Border card)
    {
        window.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not (TextBox or PasswordBox or InkCanvas))
            {
                window.DragMove();
            }
        };
        window.Loaded += (_, _) =>
        {
            window.Activate();
            var duration = TimeSpan.FromMilliseconds(140);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, duration));
            var scale = (ScaleTransform)card.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
        };
    }

    /// <summary>Title block: orange round icon, small caption, bold title, close cross.</summary>
    public static DockPanel Header(string glyph, string caption, string title, Action close)
    {
        var icon = new Border
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Child = new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = 17, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        icon.SetResourceReference(Border.BackgroundProperty, "Accent");

        var cross = new TextBlock { Text = "", FontFamily = IconFont, FontSize = 12, Padding = new Thickness(10), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
        cross.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        cross.MouseEnter += (_, _) => cross.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
        cross.MouseLeave += (_, _) => cross.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        cross.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // a click, not the start of a window drag
            close();
        };

        var captionText = new TextBlock { Text = caption, FontSize = 12 };
        captionText.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        var titles = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { captionText, new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap } },
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(cross, Dock.Right);
        header.Children.Add(icon);
        header.Children.Add(cross);
        header.Children.Add(titles);
        return header;
    }

    public static TextBlock Muted(string text, double size = 12.5)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        return block;
    }

    /// <summary>Rounded text field, outlined in orange while typing. Style it with <see cref="StyleField"/>.</summary>
    public static ControlTemplate FieldTemplate(bool password)
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        border.SetValue(Border.PaddingProperty, new Thickness(11, 0, 11, 0));
        border.SetResourceReference(Border.BackgroundProperty, "Tile.Background");
        border.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        var host = new FrameworkElementFactory(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
        border.AppendChild(host);

        var template = new ControlTemplate(password ? typeof(PasswordBox) : typeof(TextBox)) { VisualTree = border };
        var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
        focus.Setters.Add(new Setter(Border.BorderBrushProperty, new DynamicResourceExtension("Accent"), "bd"));
        template.Triggers.Add(focus);
        return template;
    }

    public static void StyleField(Control field, double height = 38)
    {
        field.Height = height;
        field.FontSize = 14.5;
        field.Padding = new Thickness(0);
        field.VerticalContentAlignment = VerticalAlignment.Center;
        field.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
        field.Template = FieldTemplate(field is PasswordBox);
        if (field is TextBox box)
        {
            box.SetResourceReference(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, "Accent");
        }
        else if (field is PasswordBox secret)
        {
            secret.SetResourceReference(PasswordBox.CaretBrushProperty, "Accent");
        }
    }

    /// <summary>Main button: orange. Others: flat, grey on hover.</summary>
    public static ControlTemplate ButtonTemplate(bool primary)
    {
        var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.SetValue(Border.PaddingProperty, new Thickness(18, 8, 18, 8));
        if (primary)
        {
            border.SetResourceReference(Border.BackgroundProperty, "Accent");
        }
        else
        {
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        }

        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        text.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, 13.5);
        text.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.SemiBold);
        if (primary)
        {
            text.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)));
        }
        else
        {
            text.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Text.Primary");
        }

        border.AppendChild(text);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension(primary ? "Accent.Strong" : "Tile.Hover"), "bd"));
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "bd"));
        template.Triggers.Add(hover);
        template.Triggers.Add(disabled);
        return template;
    }

    public static Button Button(string label, bool primary, bool isDefault = false, bool isCancel = false) => new()
    {
        Content = label,
        Template = ButtonTemplate(primary),
        IsDefault = isDefault,
        IsCancel = isCancel,
        Cursor = Cursors.Hand,
        Margin = new Thickness(6, 0, 0, 0),
    };
}
