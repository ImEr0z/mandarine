using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mandarine.App.Views;

/// <summary>
/// One slice of the format wheel. Drawn by hand (an annular sector with rounded corners) and hit-tested
/// on its exact shape, so the mouse and dragged files only "see" the slice, never the empty corners.
/// It is a real Button: Narrator, Tab, Enter and Space work as on any other button.
/// </summary>
internal sealed class WheelSegment : Button
{
    public static readonly DependencyProperty IsHotProperty = DependencyProperty.Register(
        nameof(IsHot), typeof(bool), typeof(WheelSegment),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, OnIsHotChanged));

    /// <summary>Called when the slice becomes the current target (hover, keyboard focus or a file dragged over it).</summary>
    public Action? Hint { get; set; }

    private static void OnIsHotChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            ((WheelSegment)d).Hint?.Invoke();
        }
    }

    private const double CornerRounding = 9;
    private const double GapPixels = 5;

    private readonly Geometry _shape;
    private readonly Geometry _hitShape;
    private readonly Geometry _outline;
    private readonly Point _labelCenter;
    private readonly string? _glyph;
    private readonly string _title;
    private readonly bool _badge;

    public WheelSegment(Point center, double innerRadius, double outerRadius, double startDegrees, double sweepDegrees,
        string? glyph, string title, bool badge)
    {
        _glyph = glyph;
        _title = title;
        _badge = badge;

        // The sector is drawn inset by half the rounding and stroked with the same brush, which rounds its corners.
        var inset = CornerRounding / 2;
        _shape = Sector(center, innerRadius + inset, outerRadius - inset, startDegrees, sweepDegrees, (GapPixels + CornerRounding) / 2);
        _hitShape = _shape.GetWidenedPathGeometry(new Pen(Brushes.Black, CornerRounding));
        _hitShape.Freeze();
        // Fill + rounded stroke as ONE shape, so a translucent fill is not drawn twice where they overlap.
        _outline = Geometry.Combine(_shape, _hitShape, GeometryCombineMode.Union, null);
        _outline.Freeze();
        var middle = (startDegrees + sweepDegrees / 2) * Math.PI / 180;
        var labelRadius = (innerRadius + outerRadius) / 2;
        _labelCenter = new Point(center.X + labelRadius * Math.Cos(middle), center.Y + labelRadius * Math.Sin(middle));

        Template = new ControlTemplate(typeof(WheelSegment));
        Focusable = true;
        Cursor = Cursors.Hand;
        FocusVisualStyle = null;
    }

    /// <summary>A file is being dragged over the slice.</summary>
    public bool IsHot
    {
        get => (bool)GetValue(IsHotProperty);
        set => SetValue(IsHotProperty, value);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        InvalidateVisual();
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        InvalidateVisual();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        InvalidateVisual();
    }

    public bool IsHighlighted => IsHot || IsMouseOver || IsKeyboardFocused;

    private static readonly Brush Sheen = Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromArgb(70, 255, 255, 255), 0), new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55)],
        new Point(0.5, 0), new Point(0.5, 1)));

    private static readonly Pen Rim = new(Frozen(new LinearGradientBrush(
        [new GradientStop(Color.FromArgb(120, 255, 255, 255), 0), new GradientStop(Color.FromArgb(18, 255, 255, 255), 1)],
        new Point(0.5, 0), new Point(0.5, 1))), 1);

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var highlighted = IsHighlighted;
        var fill = Brush(highlighted ? "Accent" : IsPressed ? "Tile.Pressed" : "Tile.Background");
        var text = highlighted ? Brushes.White : Brush("Text.Primary");

        // Liquid-glass look: translucent tile, soft light from the top, thin lit rim. Plain colors in high contrast.
        var glass = !SystemParameters.HighContrast && fill is SolidColorBrush;
        if (glass)
        {
            var color = ((SolidColorBrush)fill).Color;
            fill = new SolidColorBrush(Color.FromArgb((byte)(highlighted ? 215 : 140), color.R, color.G, color.B));
        }

        drawing.DrawGeometry(fill, null, _outline);
        if (glass)
        {
            drawing.DrawGeometry(Sheen, null, _outline);
            drawing.DrawGeometry(null, Rim, _outline);
        }
        else if (!highlighted)
        {
            drawing.DrawGeometry(null, new Pen(Brush("Tile.Border"), 1), _shape);
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Formats are just their name, big and bold (as on Tangerine); tools get an icon above a smaller label.
        var glyph = _glyph is null ? null : Text(_glyph, (FontFamily)FindResource("IconFont"), 17, FontWeights.Normal, text, dpi);
        var titleSize = glyph is null ? 15 : _title.Length > 9 ? 10.5 : 12;
        var title = Text(_title, (FontFamily)FindResource("TextFont"), titleSize, FontWeights.SemiBold, text, dpi);
        title.MaxTextWidth = 84;
        title.MaxLineCount = 2;
        title.TextAlignment = TextAlignment.Center;
        title.Trimming = TextTrimming.CharacterEllipsis;

        // Center the visible ink, not the text box: the box has more room above the caps than below the baseline.
        var inkTop = title.Baseline - 0.72 * titleSize;
        var inkHeight = title.Height - inkTop - 0.2 * titleSize;
        var glyphHeight = glyph is null ? 0 : glyph.Height + 3;
        var top = _labelCenter.Y - (glyphHeight + inkHeight) / 2;
        if (glyph is not null)
        {
            drawing.DrawText(glyph, new Point(_labelCenter.X - glyph.Width / 2, top));
        }

        drawing.DrawText(title, new Point(_labelCenter.X - title.MaxTextWidth / 2, top + glyphHeight - inkTop));

        if (_badge)
        {
            // Needs a free tool installed on first use (small download arrow).
            var badge = Text("", (FontFamily)FindResource("IconFont"), 10, FontWeights.Normal, text, dpi);
            drawing.DrawText(badge, new Point(_labelCenter.X + 30, top - 4));
        }
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        _hitShape.FillContains(hitTestParameters.HitPoint) || _shape.FillContains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    private Brush Brush(string key) => (Brush)FindResource(key);

    private static FormattedText Text(string text, FontFamily family, double size, FontWeight weight, Brush brush, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal), size, brush, dpi);

    /// <summary>
    /// Annular sector; angles in degrees, 0 = right, clockwise (screen coordinates). Its straight edges are
    /// pulled in by <paramref name="edgeOffset"/> pixels, so neighbours are separated by an even-width gap.
    /// </summary>
    private static Geometry Sector(Point c, double r1, double r2, double start, double sweep, double edgeOffset)
    {
        static Point At(Point c, double r, double degrees)
        {
            var a = degrees * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
        }

        // On a circle of radius r, moving the edge by d pixels means turning it by asin(d / r).
        double Shift(double r) => Math.Asin(Math.Min(1, edgeOffset / r)) * 180 / Math.PI;

        var end = start + sweep;
        var large = sweep - 2 * Shift(r2) > 180;
        var figure = new PathFigure { StartPoint = At(c, r2, start + Shift(r2)), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(c, r2, end - Shift(r2)), new Size(r2, r2), 0, large, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(At(c, r1, end - Shift(r1)), true));
        figure.Segments.Add(new ArcSegment(At(c, r1, start + Shift(r1)), new Size(r1, r1), 0, large, SweepDirection.Counterclockwise, true));
        var geometry = new PathGeometry([figure]);
        geometry.Freeze();
        return geometry;
    }
}
