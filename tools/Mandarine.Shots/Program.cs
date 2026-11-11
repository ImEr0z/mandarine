using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mandarine.App.Services;
using Mandarine.App.Views;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace Shots;

/// <summary>
/// Renders Mandarine's own windows off screen (shown far outside the desktop, never activated) and saves them as PNG,
/// plus frame sequences for the animated GIFs of the README. No mouse, no keyboard, nothing of the user's desktop.
/// </summary>
internal static class Program
{
    private static string _out = string.Empty;
    private static string _work = string.Empty;
    private static string _frames = string.Empty;
    private static Mandarine.App.App _app = null!;
    private static string _drive = string.Empty;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        _out = Path.GetFullPath(args.Length > 0 ? args[0] : "docs/images");
        Directory.CreateDirectory(_out);
        Console.SetOut(new StreamWriter(Path.Combine(Path.GetTempPath(), "mandarine-shots.log")) { AutoFlush = true }); // a WinExe has no console
        _work = Path.Combine(Path.GetTempPath(), "mandarine-shots");
        _frames = Path.Combine(_work, "frames");
        Directory.CreateDirectory(_out);
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }

        Directory.CreateDirectory(_frames);

        // Without a message loop, awaits in the windows would resume on thread-pool threads.
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        _app = new Mandarine.App.App();
        _app.InitializeComponent();
        _app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri("/Mandarine;component/Themes/Dark.xaml", UriKind.Relative) };
        UiSounds.Enabled = false;
        GlobalFontSettings.UseWindowsFontsUnderWindows = true;

        _drive = MapDrive();
        var files = MakeSamples();
        var only = args.Skip(1).ToHashSet();
        foreach (var (name, action) in Steps(files))
        {
            if (only.Count > 0 && !only.Contains(name))
            {
                continue;
            }

            try
            {
                action();
                Console.WriteLine("ok   " + name);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + name + " : " + ex.GetType().Name + " " + ex.Message);
            }
        }

        Console.WriteLine("frames for GIFs: " + _frames);
        UnmapDrive();
        return 0;
    }

    /// <summary>Folder captions show full paths: map the work folder to a spare drive letter so no user name appears.</summary>
    private static string MapDrive()
    {
        foreach (var letter in "YZX")
        {
            if (Directory.Exists(letter + @":\"))
            {
                continue;
            }

            var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("subst", $"{letter}: \"{_work}\"") { CreateNoWindow = true, UseShellExecute = false });
            result?.WaitForExit();
            if (Directory.Exists(letter + @":\"))
            {
                return letter + @":\Demo";
            }
        }

        return Path.Combine(_work, "Demo");
    }

    private static void UnmapDrive()
    {
        if (_drive.Length > 2 && _drive[1] == ':')
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("subst", $"{_drive[0]}: /d") { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit();
        }
    }

    private sealed record Samples(string Landscape, string Tilted, string Contract, string ContractNew, string Folder, string Duplicates, string[] Photos, string Prose);

    private static IEnumerable<(string, Action)> Steps(Samples s)
    {
        yield return ("wheel", () => Wheel(s));
        yield return ("toast", () => Toast());
        yield return ("settings", () => Settings());
        yield return ("rename", () => Rename(s));
        yield return ("sort", () => Still("sort", new FolderSortWindow(s.Folder), 900));
        yield return ("duplicates", () => Still("duplicates", new DuplicatesWindow(s.Duplicates), 2500));
        yield return ("metadata", () => Still("metadata", new PdfMetadataWindow(s.Contract), 600));
        yield return ("watermark", () => Still("watermark", new PdfPreviewWindow("pdf.watermark", s.Contract), 3500));
        yield return ("pdf-crop", () => Still("pdf-crop", new PdfPreviewWindow("pdf.crop", s.Contract), 3500));
        yield return ("adjust", () => Adjust(s));
        yield return ("crop", () => Crop(s));
        yield return ("annotate", () => Annotate(s));
        yield return ("redact", () => Redact(s));
        yield return ("sign", () => Sign());
        yield return ("organize", () => Still("organize", new PdfOrganizerWindow(s.Contract) { Width = 720, Height = 400 }, 4500));
        yield return ("compare", () => Still("compare", new PdfCompareWindow(s.Contract, s.ContractNew) { Width = 820, Height = 330 }, 3000));
        yield return ("summary", () => Still("summary", new PdfSummaryWindow(s.Prose), 3000));
        yield return ("editor", () => Still("editor", new PdfEditorWindow(s.Contract), 2500));
        yield return ("input", () => InputBox());
        yield return ("gif-wheel", () => GifWheel(s));
        yield return ("gif-straighten", () => GifStraighten(s));
        yield return ("gif-annotate", () => GifAnnotate(s));
        yield return ("gif-sign", () => GifSign());
        yield return ("gif-rename", () => GifRename(s));
        yield return ("gif-watermark", () => GifWatermark(s));
    }

    // ---------------------------------------------------------------- stills

    private static void Still(string name, Window window, int wait)
    {
        Reveal(window, wait);
        Save(name, Render(Root(window)), chrome: window.WindowStyle != WindowStyle.None);
        window.Close();
    }

    private static void Wheel(Samples s)
    {
        var panel = new ConverterPanel(_app, PanelMode.Picker);
        typeof(ConverterPanel).GetMethod("LoadFiles", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, [(IReadOnlyList<string>)new[] { s.Landscape }, false]);
        Reveal(panel, 700);
        ((FrameworkElement)panel.FindName("Root")).Opacity = 1;
        Pump(300);
        Save("wheel", Render(Root(panel)), chrome: false);
        panel.Close();
    }

    private static void Toast()
    {
        var toast = Make<Window>(typeof(ToastWindow), "Converti en JPG", "paysage.png → paysage.jpg", @"C:\x\paysage.jpg", true);
        var card = (FrameworkElement)toast.Content;
        card.Opacity = 1;
        ((ScaleTransform)card.RenderTransform).ScaleX = ((ScaleTransform)card.RenderTransform).ScaleY = 1;
        card.Measure(new Size(392, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        Save("toast", Render(card), chrome: false, compact: true);
    }

    private static void Settings()
    {
        var window = new SettingsWindow(new AppSettings(), _ => Task.FromResult(true), _ => { });
        Reveal(window, 3500);
        Save("settings", OnSolid(Render(Root(window)), Color.FromRgb(0x2B, 0x2B, 0x2E)), chrome: true);
        window.Close();
    }

    private static void Rename(Samples s)
    {
        var window = new BulkRenameWindow(s.Photos);
        Reveal(window, 600);
        Field<TextBox>(window, "_template").Text = "Vacances {n}";
        Pump(400);
        Save("rename", Render(Root(window)), chrome: false);
        window.Close();
    }

    private static void Adjust(Samples s)
    {
        var window = new ImageAdjustWindow(s.Tilted);
        Reveal(window, 1500);
        var sliders = Field<Dictionary<string, Slider>>(window, "_sliders");
        sliders["a"].Value = -4;
        sliders["b"].Value = 12;
        sliders["s"].Value = 25;
        Pump(1500);
        Save("adjust", Render(Root(window)), chrome: true);
        window.Close();
    }

    private static void Crop(Samples s)
    {
        var window = new ImageCropWindow(s.Landscape);
        Reveal(window, 900);
        typeof(ImageCropWindow).GetMethod("SetRatio", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, [16.0 / 9]);
        Pump(300);
        Save("crop", Render(Root(window)), chrome: true);
        window.Close();
    }

    private static void Annotate(Samples s)
    {
        var window = new ImageAnnotateWindow(s.Landscape, blur: false);
        Reveal(window, 1000);
        foreach (var mark in AnnotationMarks(window))
        {
            Invoke(window, "Add", mark);
        }

        Pump(600);
        Save("annotate", Render(Root(window)), chrome: true);
        window.Close();
    }

    private static void Redact(Samples s)
    {
        var window = new PdfRedactWindow(s.Contract);
        Reveal(window, 2500);
        var marks = Field<Dictionary<int, List<Rect>>>(window, "_marks");
        marks[0] = [new Rect(0.098, 0.124, 0.52, 0.03), new Rect(0.098, 0.203, 0.83, 0.04), new Rect(0.098, 0.382, 0.83, 0.02)];
        Invoke(window, "Refresh");
        Pump(500);
        Save("redact", Render(Root(window)), chrome: true);
        window.Close();
    }

    private static void Sign()
    {
        var pad = Make<Window>(typeof(SignaturePad), new object?[] { null });
        Reveal(pad, 600);
        var ink = Field<InkCanvas>(pad, "_ink");
        ink.Strokes.Add(new Stroke(SignaturePoints(1.0)) { DrawingAttributes = { Color = Color.FromRgb(0x1F, 0x3A, 0x93), Width = 2.6, Height = 2.6, FitToCurve = true } });
        Pump(500);
        Save("sign", Render(Root(pad)), chrome: false);
        pad.Close();
    }

    private static void InputBox()
    {
        var dialog = Make<Window>(typeof(InputDialog), "Supprimer des pages", new Mandarine.Core.Actions.ActionPrompt("Pages à supprimer", "Exemple : 2, 4-6, 9-"), "\uE74D");
        var card = (FrameworkElement)dialog.Content;
        card.Opacity = 1;
        ((ScaleTransform)card.RenderTransform).ScaleX = ((ScaleTransform)card.RenderTransform).ScaleY = 1;
        card.Measure(new Size(440, double.PositiveInfinity));
        card.Arrange(new Rect(card.DesiredSize));
        Save("input", Render(card), chrome: false, compact: true);
    }

    // ---------------------------------------------------------------- GIFs (frame sequences, assembled by scripts/make-gifs.py)

    /// <summary>The wheel turning through its pages (Shift on the real wheel): every format and tool of an image.</summary>
    private static void GifWheel(Samples s)
    {
        var panel = new ConverterPanel(_app, PanelMode.Picker);
        typeof(ConverterPanel).GetMethod("LoadFiles", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(panel, [(IReadOnlyList<string>)new[] { s.Landscape }, false]);
        Reveal(panel, 700);
        ((FrameworkElement)panel.FindName("Root")).Opacity = 1;
        Pump(300);
        var name = "wheel";
        for (var page = 0; page < 5; page++)
        {
            Frame(name, panel, hold: 14);
            Invoke(panel, "NextPage");
            Pump(250);
        }

        panel.Close();
    }

    private static void GifStraighten(Samples s)
    {
        var window = new ImageAdjustWindow(s.Tilted);
        Reveal(window, 1500);
        var sliders = Field<Dictionary<string, Slider>>(window, "_sliders");
        var name = "straighten";
        Frame(name, window, hold: 3);
        for (var angle = -0.5; angle >= -4.01; angle -= 0.5)
        {
            sliders["a"].Value = angle;
            Pump(700);
            Frame(name, window);
        }

        Frame(name, window, hold: 3);
        for (var step = 1; step <= 6; step++)
        {
            sliders["b"].Value = step * 2;
            sliders["s"].Value = step * 4.5;
            Pump(700);
            Frame(name, window);
        }

        Frame(name, window, hold: 6);
        window.Close();
    }

    private static void GifAnnotate(Samples s)
    {
        var window = new ImageAnnotateWindow(s.Landscape, blur: false);
        Reveal(window, 1000);
        var name = "annotate";
        Frame(name, window, hold: 3);
        foreach (var mark in AnnotationMarks(window))
        {
            Invoke(window, "Add", mark);
            Pump(350);
            Frame(name, window, hold: 3);
        }

        Frame(name, window, hold: 5);
        window.Close();
    }

    private static void GifSign()
    {
        var pad = Make<Window>(typeof(SignaturePad), new object?[] { null });
        Reveal(pad, 600);
        var ink = Field<InkCanvas>(pad, "_ink");
        var name = "sign";
        Frame(name, pad, hold: 3);
        for (var progress = 0.1; progress <= 1.001; progress += 0.1)
        {
            ink.Strokes.Clear();
            ink.Strokes.Add(new Stroke(SignaturePoints(progress)) { DrawingAttributes = { Color = Color.FromRgb(0x1F, 0x3A, 0x93), Width = 2.6, Height = 2.6, FitToCurve = true } });
            Pump(150);
            Frame(name, pad);
        }

        Frame(name, pad, hold: 8);
        pad.Close();
    }

    private static void GifRename(Samples s)
    {
        var window = new BulkRenameWindow(s.Photos);
        Reveal(window, 600);
        var box = Field<TextBox>(window, "_template");
        var name = "rename";
        Frame(name, window, hold: 3);
        foreach (var text in new[] { "{nom} ", "{nom} {", "{nom} {n", "{nom} {n}", "{nom} - {n}", "Vacances - {n}", "Vacances {n}" })
        {
            box.Text = text;
            Pump(150);
            Frame(name, window, hold: 2);
        }

        Frame(name, window, hold: 8);
        window.Close();
    }

    private static void GifWatermark(Samples s)
    {
        var window = new PdfPreviewWindow("pdf.watermark", s.Contract);
        Reveal(window, 3000);
        var box = Field<TextBox>(window, "_value");
        var name = "watermark";
        Frame(name, window, hold: 4);
        foreach (var text in new[] { "CONF", "CONFIDEN", "CONFIDENTIEL" })
        {
            box.Text = text;
            Pump(1500);
            Frame(name, window, hold: 3);
        }

        Frame(name, window, hold: 6);
        window.Close();
    }

    // ---------------------------------------------------------------- rendering

    /// <summary>Shows the window far outside the desktop, without activating it, and lets its async work finish.</summary>
    private static void Reveal(Window window, int wait)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        Pump(wait);
    }

    /// <summary>A translucent window (system backdrop) painted on a solid colour, as the desktop would show it.</summary>
    private static BitmapSource OnSolid(BitmapSource image, Color color)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var rect = new Rect(0, 0, image.PixelWidth / 2.0, image.PixelHeight / 2.0);
            dc.DrawRectangle(new SolidColorBrush(color), null, rect);
            dc.DrawImage(image, rect);
        }

        var target = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 192, 192, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static FrameworkElement Root(Window window) => (FrameworkElement)VisualTreeHelper.GetChild(window, 0);

    private static BitmapSource Render(FrameworkElement element, double scale = 2)
    {
        element.UpdateLayout();
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight * scale));
        var target = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        target.Render(element);
        target.Freeze();
        return target;
    }

    /// <summary>The image on a soft orange backdrop, with a shadow, ready for the README.</summary>
    private static BitmapSource Showcase(BitmapSource image, bool chrome, bool compact, double scale = 2, Size? canvas = null)
    {
        var width = image.PixelWidth / scale;
        var height = image.PixelHeight / scale;
        var pad = compact ? 36.0 : 44.0;
        var picture = new Image { Source = image, Width = width, Height = height, Stretch = Stretch.Fill };
        FrameworkElement content = picture;
        if (chrome)
        {
            content = new Border
            {
                CornerRadius = new CornerRadius(10),
                Child = picture,
                Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 10, Opacity = .38, Color = Color.FromRgb(0x5A, 0x2A, 0x00) },
                Clip = new RectangleGeometry(new Rect(0, 0, width, height), 10, 10),
            };
            // The shadow must not be clipped with the corners: clip the picture, shadow the border.
            content.Clip = null;
            picture.Clip = new RectangleGeometry(new Rect(0, 0, width, height), 10, 10);
        }

        var size = canvas ?? new Size(width + (pad * 2), height + (pad * 2));
        var backdrop = new Grid
        {
            Width = size.Width,
            Height = size.Height,
            Background = new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0xFF, 0xF0, 0xDF), 0),
                new GradientStop(Color.FromRgb(0xFF, 0xD2, 0xA6), 1),
            ], new Point(0, 0), new Point(1, 1)),
            Children = { new Border { Child = content, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } },
        };
        backdrop.Measure(size);
        backdrop.Arrange(new Rect(size));
        return Render(backdrop, scale);
    }

    private static void Save(string name, BitmapSource image, bool chrome, bool compact = false)
    {
        var composed = Showcase(image, chrome, compact);
        using var stream = File.Create(Path.Combine(_out, name + ".png"));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(composed));
        encoder.Save(stream);
    }

    private static readonly Dictionary<string, int> FrameCounts = [];
    private static readonly Dictionary<string, Size> FrameSizes = [];

    /// <summary>One frame of a GIF: the window at normal size on the backdrop; every frame has the same canvas.</summary>
    private static void Frame(string gif, Window window, int hold = 1)
    {
        var chrome = window.WindowStyle != WindowStyle.None;
        var image = Render(Root(window), 1);
        if (!FrameSizes.TryGetValue(gif, out var canvas))
        {
            canvas = FrameSizes[gif] = new Size((image.PixelWidth) + 72, (image.PixelHeight) + 72);
        }

        var composed = Showcase(image, chrome, compact: true, scale: 1, canvas: canvas);
        var directory = Path.Combine(_frames, gif);
        Directory.CreateDirectory(directory);
        for (var i = 0; i < hold; i++)
        {
            var index = FrameCounts.GetValueOrDefault(gif);
            FrameCounts[gif] = index + 1;
            using var stream = File.Create(Path.Combine(directory, $"{index:000}.png"));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composed));
            encoder.Save(stream);
        }
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(milliseconds), DispatcherPriority.Normal, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    // ---------------------------------------------------------------- reflection helpers (this tool only reads app internals)

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner)!;

    private static void Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(owner, args);

    private static T Make<T>(Type type, params object?[] args) =>
        (T)Activator.CreateInstance(type, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, args, null)!;

    private static IEnumerable<object> AnnotationMarks(ImageAnnotateWindow window)
    {
        object New(string type, params object[] args) =>
            Activator.CreateInstance(typeof(ImageAnnotateWindow).GetNestedType(type, BindingFlags.NonPublic)!, args)!;

        var red = Color.FromRgb(0xE5, 0x39, 0x35);
        var yellow = Color.FromRgb(0xF9, 0xD0, 0x1C);
        yield return New("ArrowMark", new Point(300, 220), new Point(900, 560), red, 6.0);
        yield return New("BoxMark", new Rect(980, 430, 330, 250), yellow, 6.0);
        yield return New("NoteMark", new Point(250, 120), "Le coucher de soleil !", Colors.White, 64.0);
        yield return New("BlurMark", new Rect(1130, 760, 330, 120));
        yield return New("MaskMark", new Rect(120, 790, 380, 90));
    }

    /// <summary>A hand-written looking signature, drawn up to <paramref name="progress"/> (0 to 1) of its length.</summary>
    private static StylusPointCollection SignaturePoints(double progress)
    {
        // Control points of a looped cursive signature with a long underline flourish, smoothed with Catmull-Rom.
        Point[] control =
        [
            new(40, 120), new(62, 70), new(88, 118), new(104, 92), new(126, 116), new(148, 66), new(176, 42), new(192, 68),
            new(172, 104), new(160, 122), new(200, 100), new(236, 60), new(256, 94), new(240, 118), new(276, 90), new(302, 66),
            new(330, 102), new(346, 74), new(376, 100), new(416, 90), new(446, 96), new(420, 126), new(340, 138), new(220, 142), new(100, 136),
        ];
        var points = new List<StylusPoint>();
        for (var i = 0; i < control.Length - 1; i++)
        {
            var p0 = control[Math.Max(0, i - 1)];
            var p1 = control[i];
            var p2 = control[i + 1];
            var p3 = control[Math.Min(control.Length - 1, i + 2)];
            for (var step = 0; step < 14; step++)
            {
                var t = step / 14.0;
                var t2 = t * t;
                var t3 = t2 * t;
                double Axis(double a, double b, double c, double d) =>
                    0.5 * ((2 * b) + ((-a + c) * t) + (((2 * a) - (5 * b) + (4 * c) - d) * t2) + ((-a + (3 * b) - (3 * c) + d) * t3));
                points.Add(new StylusPoint(Axis(p0.X, p1.X, p2.X, p3.X), Axis(p0.Y, p1.Y, p2.Y, p3.Y)));
            }
        }

        var count = Math.Max(2, (int)(points.Count * Math.Min(1, progress)));
        return new StylusPointCollection(points.Take(count));
    }

    // ---------------------------------------------------------------- sample files

    private static Samples MakeSamples()
    {
        var landscape = Path.Combine(_work, "paysage.png");
        var tilted = Path.Combine(_work, "photo-penchee.png");
        SavePng(Landscape(1600, 1000, 0), landscape);
        SavePng(Seascape(1600, 1000, 4), tilted);

        var contract = Path.Combine(_work, "Contrat de location.pdf");
        var contractNew = Path.Combine(_work, "Contrat de location (v2).pdf");
        MakeContract(contract, revised: false);
        MakeContract(contractNew, revised: true);

        var folder = Path.Combine(_drive, "Téléchargements");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "vacances-01.jpg", "vacances-02.jpg", "facture-mars.pdf", "cv.docx", "chanson.mp3", "film.mp4", "archive.zip", "notes.txt", "logo.png" })
        {
            File.WriteAllText(Path.Combine(folder, name), name);
        }

        var duplicates = Path.Combine(_drive, "Documents");
        Directory.CreateDirectory(Path.Combine(duplicates, "Anciens"));
        File.Copy(contract, Path.Combine(duplicates, "Contrat de location.pdf"));
        File.Copy(contract, Path.Combine(duplicates, "Contrat de location - Copie.pdf"));
        File.Copy(contract, Path.Combine(duplicates, "Anciens", "contrat.pdf"));
        File.Copy(landscape, Path.Combine(duplicates, "paysage.png"));
        File.Copy(landscape, Path.Combine(duplicates, "paysage (2).png"));
        File.WriteAllText(Path.Combine(duplicates, "liste.txt"), "pain, lait, oeufs");

        var photosDirectory = Path.Combine(_work, "Photos");
        Directory.CreateDirectory(photosDirectory);
        var photos = new[] { "IMG_4012.jpg", "IMG_4013.jpg", "IMG_4019.jpg", "IMG_4020.jpg", "IMG_4021.jpg" }
            .Select(n => Path.Combine(photosDirectory, n)).ToArray();
        foreach (var photo in photos)
        {
            File.WriteAllText(photo, "x");
        }

        var prose = Path.Combine(_work, "Le télétravail.pdf");
        MakeProse(prose);
        return new Samples(landscape, tilted, contract, contractNew, folder, duplicates, photos, prose);
    }

    private static void SavePng(BitmapSource image, string path)
    {
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    /// <summary>A sunset landscape drawn in code, optionally tilted (a crooked photo to straighten).</summary>
    private static BitmapSource Landscape(int width, int height, double tilt)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x5C)), null, new Rect(0, 0, width, height));
            if (tilt != 0)
            {
                dc.PushTransform(new TransformGroup { Children = { new ScaleTransform(1.22, 1.22, width / 2.0, height / 2.0), new RotateTransform(tilt, width / 2.0, height / 2.0) } });
            }

            dc.DrawRectangle(new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0x1B, 0x2A, 0x5C), 0),
                new GradientStop(Color.FromRgb(0x8E, 0x3F, 0x8F), 0.38),
                new GradientStop(Color.FromRgb(0xE8, 0x61, 0x8C), 0.62),
                new GradientStop(Color.FromRgb(0xFF, 0xB3, 0x5C), 0.82),
            ], 90), null, new Rect(-width, -height, width * 3, height * 3));
            var sun = new Point(width * 0.62, height * 0.62);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xFF, 0xF4, 0xC8), Color.FromArgb(0, 0xFF, 0xD0, 0x70)), null, sun, width * 0.16, width * 0.16);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xB8)), null, sun, width * 0.055, width * 0.055);
            foreach (var (x, y, w) in new[] { (0.18, 0.25, 0.22), (0.42, 0.18, 0.16), (0.78, 0.30, 0.2) })
            {
                dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(70, 255, 220, 230)), null, new Point(width * x, height * y), width * w, height * 0.035);
            }

            var layers = new[] { (0.62, Color.FromRgb(0x5B, 0x2A, 0x6B), 0.12), (0.70, Color.FromRgb(0x3A, 0x1D, 0x52), 0.09), (0.80, Color.FromRgb(0x1D, 0x10, 0x33), 0.07) };
            for (var layer = 0; layer < layers.Length; layer++)
            {
                var (baseline, color, amplitude) = layers[layer];
                var figure = new StreamGeometry();
                using (var g = figure.Open())
                {
                    g.BeginFigure(new Point(-width, height * 2), true, true);
                    for (var x = -width; x <= width * 2; x += 20)
                    {
                        var wave = Math.Sin((x * 0.004) + (layer * 1.7)) + (0.5 * Math.Sin((x * 0.011) + layer));
                        g.LineTo(new Point(x, (height * baseline) - (wave * height * amplitude * 0.5)), true, false);
                    }
                }

                figure.Freeze();
                dc.DrawGeometry(new SolidColorBrush(color), null, figure);
            }

            for (var tree = 0; tree < 9; tree++)
            {
                var x = width * (0.08 + (tree * 0.11));
                var baseY = height * 0.93;
                var size = height * (0.07 + ((tree % 3) * 0.02));
                var figure = new StreamGeometry();
                using (var g = figure.Open())
                {
                    g.BeginFigure(new Point(x, baseY - (size * 2)), true, true);
                    g.LineTo(new Point(x + size * 0.6, baseY), true, false);
                    g.LineTo(new Point(x - size * 0.6, baseY), true, false);
                }

                figure.Freeze();
                dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x0E, 0x08, 0x1C)), null, figure);
            }

            if (tilt != 0)
            {
                dc.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    /// <summary>A lighthouse on a sea with a straight horizon: a crooked photo whose tilt is easy to see.</summary>
    private static BitmapSource Seascape(int width, int height, double tilt)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x5C)), null, new Rect(0, 0, width, height));
            if (tilt != 0)
            {
                dc.PushTransform(new TransformGroup { Children = { new ScaleTransform(1.22, 1.22, width / 2.0, height / 2.0), new RotateTransform(tilt, width / 2.0, height / 2.0) } });
            }

            var horizon = height * 0.62;
            dc.DrawRectangle(new LinearGradientBrush(
            [
                new GradientStop(Color.FromRgb(0x2B, 0x3A, 0x7C), 0),
                new GradientStop(Color.FromRgb(0xE8, 0x61, 0x8C), 0.7),
                new GradientStop(Color.FromRgb(0xFF, 0xC0, 0x70), 1),
            ], 90), null, new Rect(-width, -height, width * 3, horizon + height));
            var sun = new Point(width * 0.68, horizon - 8);
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xFF, 0xF4, 0xC8), Color.FromArgb(0, 0xFF, 0xD0, 0x70)), null, sun, width * 0.2, width * 0.2);
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xFF, 0xF1, 0xB8)), null, sun, width * 0.06, width * 0.06);
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x24, 0x3B, 0x7A), Color.FromRgb(0x0F, 0x1B, 0x44), 90), null, new Rect(-width, horizon, width * 3, height * 2));
            for (var i = 0; i < 9; i++)
            {
                var w = width * (0.2 - (i * 0.018));
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 0xFF, 0xD0, 0x80)), null, new Rect(sun.X - (w / 2), horizon + 10 + (i * 22), w, 6));
            }

            // Rocks and a lighthouse: strong vertical lines.
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x0E, 0x2A)), null, new Rect(width * 0.08, horizon - 40, width * 0.3, 80));
            var towerX = width * 0.2;
            dc.DrawRectangle(Brushes.White, null, new Rect(towerX, horizon - 330, 56, 300));
            foreach (var band in new[] { 60, 150, 240 })
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0x3B, 0x30)), null, new Rect(towerX, horizon - 330 + band, 56, 38));
            }

            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x0E, 0x2A)), null, new Rect(towerX - 10, horizon - 366, 76, 36));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x80)), null, new Rect(towerX + 8, horizon - 358, 40, 20));
            if (tilt != 0)
            {
                dc.Pop();
            }
        }

        var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static readonly string[] Prose =
    [
        "Le télétravail s'est installé durablement dans les entreprises françaises depuis quelques années. Il permet à de nombreux salariés de mieux organiser leur journée et de gagner un temps précieux sur les trajets.",
        "Les entreprises y trouvent elles aussi des avantages. Elles réduisent leurs coûts immobiliers et constatent souvent une meilleure concentration des équipes sur les tâches qui demandent du calme.",
        "Le travail à distance a pourtant des limites. Sans rencontres régulières, les liens entre collègues se distendent et les nouveaux arrivants ont du mal à trouver leurs repères dans l'équipe.",
        "Beaucoup d'organisations choisissent donc un compromis. Deux ou trois jours à la maison, le reste de la semaine au bureau, avec des temps forts pour travailler ensemble sur les projets importants.",
        "Réussir le télétravail demande des règles claires. Il faut définir des horaires de disponibilité, bien choisir les outils de communication et apprendre à déconnecter pour protéger sa vie personnelle.",
        "Les managers jouent un rôle central dans cette transition. Ils doivent faire confiance, fixer des objectifs précis et mesurer les résultats plutôt que le temps passé devant l'écran.",
        "Au final, le télétravail n'est ni une solution miracle ni un danger pour l'entreprise. C'est une nouvelle façon de travailler qu'il faut adapter à chaque métier et à chaque équipe.",
    ];

    /// <summary>One page of plain prose, to show the summary on a text that reads like an article.</summary>
    private static void MakeProse(string path)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var formatter = new XTextFormatter(graphics);
            var top = 80.0;
            foreach (var paragraph in Prose)
            {
                formatter.DrawString(paragraph, new XFont("Arial", 11), XBrushes.Black, new XRect(60, top, 475, 90), XStringFormats.TopLeft);
                top += 85;
            }
        }

        document.Save(path);
    }

    private static readonly string[] Articles =
    [
        "Le bailleur loue au locataire un appartement de trois pièces situé au deuxième étage d'un immeuble, destiné exclusivement à l'habitation principale du locataire.",
        "Le contrat est conclu pour une durée de trois ans à compter de la remise des clés. Il se renouvelle ensuite par tacite reconduction, sauf congé donné par l'une des parties dans les délais légaux.",
        "Le loyer mensuel est de {rent} euros, charges comprises. Il est payable d'avance, le cinq de chaque mois, par virement sur le compte indiqué par le bailleur.",
        "Le locataire verse un dépôt de garantie équivalent à un mois de loyer. Ce dépôt est restitué dans un délai de deux mois après la remise des clés, déduction faite des sommes dues.",
        "Le locataire s'engage à user paisiblement des lieux, à entretenir le logement et à souscrire une assurance contre les risques locatifs. Il répond des dégradations survenues pendant la durée du contrat.",
        "{extra}Un état des lieux d'entrée est établi contradictoirement lors de la remise des clés. Un état des lieux de sortie est établi dans les mêmes conditions lors de la restitution du logement.",
    ];

    /// <summary>A three-page contract: titles, paragraphs, a signature line, bookmarks and document information.</summary>
    private static void MakeContract(string path, bool revised)
    {
        using var document = new PdfDocument();
        document.Info.Title = "Contrat de location";
        document.Info.Author = "Camille Martin";
        document.Info.Subject = "Location d'un appartement";
        document.Info.Creator = "Microsoft Word 2021";
        var titleFont = new XFont("Arial", 22, XFontStyleEx.Bold);
        var headingFont = new XFont("Arial", 13, XFontStyleEx.Bold);
        var bodyFont = new XFont("Arial", 11);
        var muted = new XSolidBrush(XColor.FromArgb(255, 90, 90, 90));
        for (var number = 0; number < 3; number++)
        {
            var page = document.AddPage();
            using var graphics = XGraphics.FromPdfPage(page);
            var formatter = new XTextFormatter(graphics) { Alignment = XParagraphAlignment.Left };
            graphics.DrawString(number == 0 ? "Contrat de location" : $"Contrat de location — page {number + 1}", titleFont, XBrushes.Black, new XRect(60, 60, 475, 30), XStringFormats.TopLeft);
            graphics.DrawLine(new XPen(XColor.FromArgb(255, 245, 138, 31), 2), 60, 96, 535, 96);
            graphics.DrawString("Entre Camille Martin (le bailleur) et Alex Durand (le locataire)", bodyFont, muted, new XRect(60, 106, 475, 20), XStringFormats.TopLeft);
            var top = 150.0;
            for (var article = 0; article < 2; article++)
            {
                var index = (number * 2) + article;
                var text = Articles[index % Articles.Length]
                    .Replace("{rent}", revised ? "920" : "850")
                    .Replace("{extra}", revised ? string.Empty : "Les animaux domestiques sont acceptés sous réserve de l'accord écrit du bailleur. ");
                graphics.DrawString($"Article {index + 1}", headingFont, XBrushes.Black, new XRect(60, top, 475, 20), XStringFormats.TopLeft);
                formatter.DrawString(text, bodyFont, XBrushes.Black, new XRect(60, top + 24, 475, 120), XStringFormats.TopLeft);
                top += 150;
            }

            if (number == 2)
            {
                graphics.DrawString("Fait à Paris, le 3 mars 2026", bodyFont, XBrushes.Black, new XRect(60, 640, 300, 20), XStringFormats.TopLeft);
                graphics.DrawString("Le locataire", bodyFont, muted, new XRect(340, 660, 200, 20), XStringFormats.TopLeft);
                graphics.DrawLine(new XPen(XColor.FromArgb(255, 150, 150, 150), 1), 340, 740, 535, 740);
            }

            document.Outlines.Add($"Articles {(number * 2) + 1} et {(number * 2) + 2}", page, true);
        }

        document.Save(path);
    }
}
