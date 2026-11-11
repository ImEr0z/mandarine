using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Mandarine.App.Views;

/// <summary>Orange card shown while a new version downloads; Mandarine restarts into it afterwards.</summary>
internal sealed class UpdateWindow : Window
{
    private const double CardWidth = 380, CardHeight = 270;

    private const double BarWidth = CardWidth - 56;

    private readonly Grid _fill = new() { HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    private readonly TextBlock _status = new() { FontSize = 12, Foreground = Brushes.White, Opacity = .9 };
    private readonly TextBlock _percent = new() { Text = "0 %", FontSize = 13, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _eta = new() { Text = "Calcul du temps restant…", FontSize = 12, Foreground = Brushes.White, Opacity = .8, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly System.Windows.Threading.DispatcherTimer _phraseTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

    private static readonly string[] Phrases =
    [
        "Épluchage des nouveautés…",
        "On presse un peu de jus frais…",
        "Séparation des quartiers…",
        "Cueillette des derniers bugs…",
        "Ajout d'une pincée de vitamines…",
        "On polit l'écorce…",
        "Mandarine mûrit à vue d'œil…",
        "Presque à maturité…",
        "Apprentissage de nouveaux formats aux tuiles…",
        "On affûte le PDF…",
        "Un petit coup de Shift, et hop…",
        "Dressage des convertisseurs…",
        "Lustrage de l'icône dans la zone de notification…",
        "Les fichiers font la queue, bien sagement…",
        "Rien ne se perd, tout se convertit…",
        "Encore un quartier et c'est prêt…",
    ];

    public UpdateWindow(string version)
    {
        Title = "Mandarine";
        Width = CardWidth + 40;
        Height = CardHeight + 40;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        SetResourceReference(FontFamilyProperty, "TextFont");

        var card = new Grid { Width = CardWidth, Height = CardHeight, Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), 20, 20) };
        card.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                [new GradientStop(Color.FromRgb(0xFF, 0xB0, 0x3A), 0), new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x14), .55), new GradientStop(Color.FromRgb(0xE8, 0x53, 0x0A), 1)],
                new Point(0, 0), new Point(1, 1)),
        });
        card.Children.Add(BuildBubbles());
        card.Children.Add(BuildContent(version));

        // The shadow lives on an outer border: the card itself is clipped.
        Content = new Border
        {
            Width = CardWidth,
            Height = CardHeight,
            CornerRadius = new CornerRadius(20),
            Background = Brushes.Black,
            Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 4, Opacity = .45, Color = Color.FromRgb(0xA9, 0x4F, 0x00) },
            Child = card,
        };

        Loaded += (_, _) => card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));

        // A new playful phrase every few seconds, cross-faded.
        var next = 0;
        _status.Text = Phrases[next];
        _phraseTimer.Tick += (_, _) =>
        {
            next = (next + 1) % Phrases.Length;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fadeOut.Completed += (_, _) =>
            {
                _status.Text = Phrases[next];
                _status.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .9, TimeSpan.FromMilliseconds(250)));
            };
            _status.BeginAnimation(OpacityProperty, fadeOut);
        };
        _phraseTimer.Start();
        Closed += (_, _) => _phraseTimer.Stop();
    }

    /// <summary>Soft bubbles and rings drifting up behind the content.</summary>
    private static Canvas BuildBubbles()
    {
        var canvas = new Canvas { IsHitTestVisible = false };
        var rng = new Random(7);
        for (var i = 0; i < 10; i++)
        {
            var size = 10 + rng.NextDouble() * 44;
            var alpha = (byte)(10 + rng.Next(22));
            var bubble = new Ellipse { Width = size, Height = size };
            if (i % 3 == 0)
            {
                bubble.Stroke = new SolidColorBrush(Color.FromArgb((byte)(alpha + 15), 255, 255, 255));
                bubble.StrokeThickness = 2;
            }
            else
            {
                bubble.Fill = new RadialGradientBrush(Color.FromArgb((byte)(alpha + 12), 255, 255, 255), Color.FromArgb(alpha, 255, 214, 150));
            }

            var sway = new TranslateTransform();
            bubble.RenderTransform = sway;
            Canvas.SetLeft(bubble, rng.NextDouble() * (CardWidth - size));
            var seconds = 7 + rng.NextDouble() * 7;
            var offset = -rng.NextDouble() * seconds;

            canvas.Children.Add(bubble);
            bubble.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(CardHeight + 10, -size - 10, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(offset) });
            sway.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-14, 14, TimeSpan.FromSeconds(2 + rng.NextDouble() * 2)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }

        return canvas;
    }

    private StackPanel BuildContent(string version)
    {
        // Still logo on a softly pulsing halo.
        var halo = new Ellipse
        {
            Width = 96,
            Height = 96,
            RenderTransformOrigin = new Point(.5, .5),
            RenderTransform = new ScaleTransform(1, 1),
            Fill = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255)),
        };
        halo.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, Pulse());
        halo.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, Pulse());

        var logo = new Image { Width = 60, Height = 60, Source = LoadLogo(), RenderTransformOrigin = new Point(.5, .5) };
        logo.Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 3, Opacity = .35, Color = Color.FromRgb(0x8A, 0x30, 0x00) };

        var logoBox = new Grid { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Children = { halo, logo } };

        var title = new TextBlock { Text = "Mise à jour de Mandarine", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        var sub = new TextBlock { Text = $"Nouvelle version {version}", FontSize = 12, Foreground = Brushes.White, Opacity = .85, HorizontalAlignment = HorizontalAlignment.Center };

        // Bar: white fill scaled to the percentage, with a warm shimmer sweeping across it.
        var shimmerMove = new TranslateTransform(-100, 0);
        var shimmer = new Rectangle
        {
            Width = 100,
            HorizontalAlignment = HorizontalAlignment.Left,
            RenderTransform = shimmerMove,
            Fill = new LinearGradientBrush(
                [new GradientStop(Colors.Transparent, 0), new GradientStop(Color.FromArgb(200, 255, 150, 40), .5), new GradientStop(Colors.Transparent, 1)],
                new Point(0, 0), new Point(1, 0)),
        };
        shimmerMove.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-100, CardWidth, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever });
        // Width (not a scale) keeps the rounded end of the bar round as it grows.
        var fill = _fill;
        fill.Children.Add(new Rectangle { Fill = Brushes.White });
        fill.Children.Add(shimmer);
        fill.SizeChanged += (_, e) => fill.Clip = new RectangleGeometry(new Rect(0, 0, e.NewSize.Width, 10), 5, 5);
        var track = new Border { Height = 10, CornerRadius = new CornerRadius(5), Margin = new Thickness(0, 16, 0, 8), ClipToBounds = false, Background = new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), Child = fill };

        var info = new Grid { Children = { _status, _percent } };

        return new StackPanel
        {
            Margin = new Thickness(28, 18, 28, 18),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { logoBox, title, sub, track, info, _eta },
        };
    }

    private static DoubleAnimation Pulse() => new(.85, 1.15, TimeSpan.FromSeconds(1.5)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };

    /// <summary>The biggest frame of the app icon.</summary>
    private static BitmapSource LoadLogo() =>
        new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/mandarine.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames.OrderByDescending(f => f.PixelWidth).First();

    /// <summary>Download done: shows the closing messages for a few seconds before the caller installs and restarts.</summary>
    public async Task FinishAsync()
    {
        _phraseTimer.Stop();
        (string Status, string Eta)[] steps =
        [
            ("Téléchargement terminé ✓", "Tout est là"),
            ("Installation en cours…", "Quelques secondes"),
            ("Installée ✓", "Mandarine est à jour"),
            ("Prêt à être utilisé !", "Redémarrage…"),
        ];

        _percent.Text = "100 %";
        _fill.BeginAnimation(WidthProperty, new DoubleAnimation(BarWidth, TimeSpan.FromMilliseconds(300)));
        foreach (var (status, eta) in steps)
        {
            _status.BeginAnimation(OpacityProperty, null);
            _status.Opacity = .9;
            _status.Text = status;
            _eta.Text = eta;
            await Task.Delay(1100);
        }
    }

    /// <summary>Called from the download thread.</summary>
    public void SetProgress(int percent) => Dispatcher.BeginInvoke(() =>
    {
        percent = Math.Clamp(percent, 0, 100);
        _fill.BeginAnimation(WidthProperty, new DoubleAnimation(BarWidth * percent / 100.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase() });
        _percent.Text = $"{percent} %";

        if (percent >= 100)
        {
            _phraseTimer.Stop();
        }
        else if (percent > 0 && _clock.Elapsed.TotalSeconds > 1)
        {
            var left = TimeSpan.FromSeconds(_clock.Elapsed.TotalSeconds * (100 - percent) / percent);
            _eta.Text = left.TotalSeconds < 5 ? "Presque terminé…"
                : left.TotalMinutes < 1 ? $"Environ {Math.Round(left.TotalSeconds / 5) * 5:0} s restantes"
                : $"Environ {Math.Ceiling(left.TotalMinutes):0} min restantes";
        }
    });
}
