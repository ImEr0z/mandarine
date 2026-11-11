using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Mandarine.App.Services;

namespace Mandarine.App.Views;

/// <summary>"Nouveautés": the latest release as a grid of tiles, plus the history of every release.</summary>
internal sealed class NewsWindow : Window
{
    private const double CardWidth = 720, CardHeight = 520;

    private static readonly Brush Ink = Frozen(Color.FromRgb(0x2A, 0x1A, 0x0E));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x7A, 0x66, 0x55));
    private static readonly Brush[] Tints =
    [
        Frozen(Colors.White),
        Frozen(Color.FromRgb(0xFF, 0xE9, 0xD2)),
        Frozen(Color.FromRgb(0xFF, 0xF3, 0xCF)),
        Frozen(Color.FromRgb(0xFF, 0xDE, 0xC2)),
    ];

    private readonly ScrollViewer _body = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Border _tabNews = Tab("Nouveautés"), _tabHistory = Tab("Historique"), _tabStats = Tab("Stats");
    private static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "Mandarine" } }, Timeout = TimeSpan.FromSeconds(10) };
    // Real downloads only; the metadata files (RELEASES, *.json) are fetched by the updater, not by people.
    private static readonly Regex DownloadAsset = new(@"Setup\.exe$|Portable\.zip$|^Mandarine-win-.*\.zip$");
    private readonly DockPanel _dock;
    private readonly Task<UIElement> _stats;
    private System.Windows.Threading.DispatcherTimer? _countdown;
    private UIElement? _countdownBar;

    public NewsWindow()
    {
        Title = "Nouveautés de Mandarine";
        Width = CardWidth + 40;
        SizeToContent = SizeToContent.Height;
        MaxHeight = CardHeight + 320;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual; // centered by hand: the height is only known after layout
        ShowInTaskbar = true;
        ContentRendered += (_, _) =>
        {
            // Lock the height the first tab needs: switching tabs must never move the chips under the cursor.
            Height = ActualHeight;
            SizeToContent = SizeToContent.Manual;
            Recenter();
        };
        SetResourceReference(FontFamilyProperty, "TextFont");

        _stats = FetchStats();
        var latest = Featured;
        var closeGlyph = new TextBlock { Text = "✕", FontSize = 14, Foreground = Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var close = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top, Child = closeGlyph };
        close.MouseEnter += (_, _) => { close.Background = Frozen(Color.FromArgb(55, 0xE0, 0x70, 0x0A)); closeGlyph.Foreground = Ink; };
        close.MouseLeave += (_, _) => { close.Background = Brushes.Transparent; closeGlyph.Foreground = Muted; };
        close.MouseLeftButtonDown += (_, e) => e.Handled = true; // a click, not the start of a window drag
        close.MouseLeftButtonUp += (_, _) => Close();

        var header = new Grid
        {
            Children =
            {
                new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = "Nouveautés", FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Ink },
                        new TextBlock { Text = $"Mandarine {latest.Version} · {latest.Date}", FontSize = 13, Foreground = Muted, Margin = new Thickness(0, 2, 0, 0) },
                    },
                },
                close,
            },
        };
        close.HorizontalAlignment = HorizontalAlignment.Right;

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 16, 0, 0), Children = { _tabNews, _tabHistory, _tabStats } };
        _tabNews.MouseLeftButtonUp += (_, _) => ShowTab(0);
        _tabHistory.MouseLeftButtonUp += (_, _) => ShowTab(1);
        _tabStats.MouseLeftButtonUp += (_, _) => ShowTab(2);

        var card = new Border
        {
            Width = CardWidth,
            Margin = new Thickness(20),
            CornerRadius = new CornerRadius(26),
            Padding = new Thickness(32, 26, 32, 26),
            Background = new LinearGradientBrush(Color.FromRgb(0xFF, 0xF8, 0xEF), Color.FromRgb(0xFF, 0xEF, 0xDC), 90),
            Effect = new DropShadowEffect { BlurRadius = 36, ShadowDepth = 6, Opacity = .35, Color = Color.FromRgb(0x8A, 0x40, 0x00) },
            Child = _dock = new DockPanel { LastChildFill = true, Children = { AtTop(header), AtTop(tabs), _body } },
        };
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not ScrollViewer and not TextBlock) { DragMove(); } };
        Content = card;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); } };
        Loaded += (_, _) => Activate();
        ShowTab(0);
    }

    /// <summary>Closes the window by itself after <paramref name="seconds"/>, with a draining bar and a seconds counter. Clicking the history tab cancels it.</summary>
    public void StartCountdown(int seconds)
    {
        var left = seconds;
        var text = new TextBlock { Text = Label(left), FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0) };
        // Width (not a scale) keeps the rounded end round while it drains.
        var fill = new Border { CornerRadius = new CornerRadius(3), Background = Frozen(Color.FromRgb(0xFF, 0x7A, 0x14)), HorizontalAlignment = HorizontalAlignment.Left, Width = CardWidth - 64 };
        var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Frozen(Color.FromArgb(40, 0xE0, 0x70, 0x0A)), Child = fill };
        var strip = new StackPanel { Margin = new Thickness(0, 12, 0, 0), Children = { track, text } };
        DockPanel.SetDock(strip, Dock.Bottom);
        _dock.Children.Insert(2, strip);
        _countdownBar = strip;

        fill.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(CardWidth - 64, 6, TimeSpan.FromSeconds(seconds)));
        _countdown = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdown.Tick += (_, _) =>
        {
            left--;
            if (left <= 0) { Close(); return; }
            text.Text = Label(left);
        };
        _countdown.Start();
        Closed += (_, _) => _countdown?.Stop();
        Recenter();

        static string Label(int s) => $"Cette fenêtre se ferme dans {s} s";
    }

    private void CancelCountdown()
    {
        _countdown?.Stop();
        if (_countdownBar is not null) { _dock.Children.Remove(_countdownBar); _countdownBar = null; }
    }

    /// <summary>Puts the window in the middle of the main screen's work area; called again whenever its height changes.</summary>
    private void Recenter() => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
    {
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
    });

    /// <summary>0 = latest news, 1 = history, 2 = download stats.</summary>
    private void ShowTab(int tab)
    {
        if (tab != 0) { CancelCountdown(); }
        Select(_tabNews, tab == 0);
        Select(_tabHistory, tab == 1);
        Select(_tabStats, tab == 2);
        if (tab == 2) { ShowStats(); }
        else { _body.Content = tab == 1 ? BuildHistory() : BuildLatest(Featured); }
        _body.ScrollToTop();
    }

    /// <summary>Download counts straight from the public GitHub releases: no server, no tracking.</summary>
    private async void ShowStats()
    {
        var content = _stats; // fetched since the window opened, so usually already done
        if (!content.IsCompleted) { _body.Content = new TextBlock { Text = "Chargement…", Foreground = Muted, FontSize = 13 }; }
        var view = await content;
        if (_tabStats.Tag is true) { _body.Content = view; } // still on the Stats tab
    }

    private async Task<UIElement> FetchStats()
    {
        try
        {
            using var doc = JsonDocument.Parse(await Http.GetStringAsync("https://api.github.com/repos/ImEr0z/mandarine/releases?per_page=100"));
            var releases = doc.RootElement.EnumerateArray().Select(r => (
                Tag: r.GetProperty("tag_name").GetString() ?? "?",
                Assets: r.GetProperty("assets").EnumerateArray()
                    .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Count: a.GetProperty("download_count").GetInt32())).ToList())).ToList();
            var rows = releases.Select(r => (r.Tag, N: r.Assets.Where(a => DownloadAsset.IsMatch(a.Name)).Sum(a => a.Count))).ToList();
            var checks = releases.Sum(r => r.Assets.Where(a => a.Name == "releases.win.json").Sum(a => a.Count));
            var tiles = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            tiles.Children.Add(StatTile("Téléchargements", rows.Sum(r => r.N).ToString(), Tints[0]));
            tiles.Children.Add(StatTile("Lancements (≈)", checks.ToString(), Tints[1]));
            tiles.Children.Add(StatTile("Versions", releases.Count.ToString(), Tints[2]));

            var list = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var (tag, n) in rows)
            {
                list.Children.Add(new TextBlock { Text = $"{tag}  ·  {n}", FontSize = 12.5, Foreground = Muted, Margin = new Thickness(4, 2, 0, 2) });
            }

            return new StackPanel { Children = { tiles, list } };
        }
        catch (Exception)
        {
            return new TextBlock { Text = "Statistiques indisponibles (pas de connexion ?)", Foreground = Muted, FontSize = 13 };
        }
    }

    private static Border StatTile(string label, string value, Brush tint) => new()
    {
        Margin = new Thickness(0, 0, 10, 10),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(18, 16, 18, 16),
        Background = tint,
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = value, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = Frozen(Color.FromRgb(0xE0, 0x70, 0x0A)) },
                new TextBlock { Text = label, FontSize = 12, Foreground = Muted },
            },
        },
    };

    /// <summary>The release shown on the first tab: 0.5.0 brought the most. Every other release is in the history tab.</summary>
    private static readonly Release Featured = ReleaseNotes.All.First(r => r.Version == "0.5.0");

    /// <summary>Bento grid: the first note is a wide orange tile, the others fill three columns.</summary>
    private static Grid BuildLatest(Release release)
    {
        var grid = new Grid();
        for (var c = 0; c < 3; c++) { grid.ColumnDefinitions.Add(new ColumnDefinition()); }

        // Headline tile (2 columns wide), then notes; a wide tile when a row would be left with one hole.
        var cell = 0;
        var row = 0;
        void Place(UIElement tile, int span)
        {
            if (cell + span > 3) { cell = 0; row++; }
            while (grid.RowDefinitions.Count <= row) { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            Grid.SetRow(tile, row);
            Grid.SetColumn(tile, cell);
            Grid.SetColumnSpan(tile, span);
            grid.Children.Add(tile);
            cell += span;
        }

        Place(HeroTile(release.Headline), 2);
        Place(NoteTile(release.Notes[0], Tints[0]), 1);
        for (var i = 1; i < release.Notes.Length; i++)
        {
            var remaining = release.Notes.Length - i;
            var wide = cell == 1 && remaining == 1; // last note alone next to one tile
            Place(NoteTile(release.Notes[i], Tints[i % Tints.Length]), wide ? 2 : 1);
        }

        return grid;
    }

    private static Border HeroTile(string headline) => new()
    {
        Margin = new Thickness(0, 0, 10, 10),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(22),
        MinHeight = 140,
        Background = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0xFF, 0xB0, 0x3A), 0), new GradientStop(Color.FromRgb(0xFF, 0x7A, 0x14), .6), new GradientStop(Color.FromRgb(0xE8, 0x53, 0x0A), 1)],
            new Point(0, 0), new Point(1, 1)),
        Child = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Children =
            {
                new TextBlock { Text = "✨", FontSize = 34, Foreground = Brushes.White },
                new TextBlock { Text = headline, FontSize = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
            },
        },
    };

    private static Border NoteTile(Note note, Brush tint) => new()
    {
        Margin = new Thickness(0, 0, 10, 10),
        CornerRadius = new CornerRadius(20),
        Padding = new Thickness(18, 16, 18, 16),
        MinHeight = 140,
        Background = tint,
        Child = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = note.Icon, FontSize = 24, Foreground = Frozen(Color.FromRgb(0xE0, 0x70, 0x0A)) },
                new TextBlock { Text = note.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, Margin = new Thickness(0, 8, 0, 3), TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = note.Text, FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap },
            },
        },
    };

    private static StackPanel BuildHistory()
    {
        var list = new StackPanel();
        foreach (var release in ReleaseNotes.All)
        {
            var lines = new StackPanel();
            foreach (var note in release.Notes)
            {
                var line = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = Muted, Margin = new Thickness(0, 3, 0, 0) };
                line.Inlines.Add(new System.Windows.Documents.Run($"{note.Icon}  ") { Foreground = Frozen(Color.FromRgb(0xE0, 0x70, 0x0A)) });
                line.Inlines.Add(new System.Windows.Documents.Run(note.Title) { FontWeight = FontWeights.SemiBold, Foreground = Ink });
                line.Inlines.Add(new System.Windows.Documents.Run($" : {note.Text}"));
                lines.Children.Add(line);
            }

            list.Children.Add(new Border
            {
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20, 16, 20, 16),
                Background = Brushes.White,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = $"Version {release.Version}", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Frozen(Color.FromRgb(0xE0, 0x70, 0x0A)) },
                        new TextBlock { Text = $"{release.Date} · {release.Headline}", FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 1, 0, 6) },
                        lines,
                    },
                },
            });
        }

        return list;
    }

    private static Border Tab(string text)
    {
        var tab = new Border
        {
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold },
        };
        tab.MouseEnter += (_, _) => Paint(tab, hover: true);
        tab.MouseLeave += (_, _) => Paint(tab, hover: false);
        tab.MouseLeftButtonDown += (_, e) => e.Handled = true; // a click, not the start of a window drag
        return tab;
    }

    private static void Select(Border tab, bool selected)
    {
        tab.Tag = selected;
        Paint(tab, tab.IsMouseOver);
    }

    /// <summary>Chip colors: orange when selected, soft orange otherwise, each one step stronger under the mouse.</summary>
    private static void Paint(Border tab, bool hover)
    {
        var selected = tab.Tag is true;
        tab.Background = selected
            ? Frozen(hover ? Color.FromRgb(0xF2, 0x66, 0x00) : Color.FromRgb(0xFF, 0x7A, 0x14))
            : Frozen(Color.FromArgb((byte)(hover ? 85 : 40), 0xE0, 0x70, 0x0A));
        ((TextBlock)tab.Child).Foreground = selected ? Brushes.White : Frozen(Color.FromRgb(0xA9, 0x4F, 0x00));
    }

    private static UIElement AtTop(UIElement element)
    {
        DockPanel.SetDock(element, Dock.Top);
        return element;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
