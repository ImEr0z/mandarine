using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Mandarine.App.Services;
using Mandarine.Core;
using Mandarine.Core.Actions;
using Mandarine.Core.Converters;

namespace Mandarine.App.Views;

/// <summary>Watermark or crop of a PDF, with the first page redrawn as you type and nothing written until "Appliquer".</summary>
internal sealed class PdfPreviewWindow : Window
{
    private readonly FileAction _action;
    private readonly string _path;
    private readonly TextBox _value = new();
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly TextBlock _problem = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x4B, 0x4B)), Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _busy = new() { Text = "Aperçu…", Opacity = 0.55, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Button _apply;
    private readonly Border _logoChip = new() { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
    private readonly TextBlock _logoName = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 210 };
    private string? _logo;
    private int _generation;

    public PdfPreviewWindow(string actionId, string path)
    {
        _path = path;
        _action = ActionCatalog.SelfContained().Find(actionId) ?? throw new InvalidOperationException($"Action inconnue : {actionId}");
        var prompt = _action.Prompt ?? throw new InvalidOperationException("Cette action n'a pas d'aperçu.");
        var isWatermark = actionId == "pdf.watermark";

        Title = $"{_action.Title} – {System.IO.Path.GetFileName(path)}";
        DialogStyle.Frameless(this, 760);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        DialogStyle.StyleField(_value);
        _value.Text = isWatermark ? "COPIE CONFORME" : "10";
        _value.TextChanged += (_, _) => Schedule();

        // ---- Preview, always the first page
        var sheet = new Border { Width = 300, Height = 400, Background = Brushes.White, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), ClipToBounds = true, Child = new Grid { Children = { _busy, _image } } };
        sheet.SetResourceReference(Border.BorderBrushProperty, "Panel.Border");
        var caption = DialogStyle.Muted("Aperçu de la première page");
        caption.HorizontalAlignment = HorizontalAlignment.Center;
        caption.Margin = new Thickness(0, 8, 0, 0);
        var left = new StackPanel { Margin = new Thickness(0, 0, 22, 0), Children = { sheet, caption } };

        // ---- Form
        var hint = DialogStyle.Muted(prompt.Hint);
        hint.Margin = new Thickness(2, 8, 0, 0);
        var form = new StackPanel { Width = 320, Children = { _value, hint } };

        if (isWatermark)
        {
            var pick = DialogStyle.Button("Utiliser une image (logo)…", primary: false);
            pick.Margin = new Thickness(0, 12, 0, 0);
            pick.HorizontalAlignment = HorizontalAlignment.Left;
            pick.Click += (_, _) => PickLogo();
            var removeLogo = new TextBlock { Text = "", FontFamily = DialogStyle.IconFont, FontSize = 11, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(8, 4, 2, 4), Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
            removeLogo.MouseLeftButtonDown += (_, e) =>
            {
                e.Handled = true;
                SetLogo(null);
            };
            _logoChip.Child = new DockPanel { Children = { removeLogo, _logoName } };
            DockPanel.SetDock(removeLogo, Dock.Right);
            _logoChip.SetResourceReference(Border.BackgroundProperty, "Accent.Soft");
            _logoName.SetResourceReference(TextBlock.ForegroundProperty, "Text.Primary");
            form.Children.Add(pick);
            form.Children.Add(_logoChip);
        }

        form.Children.Add(_problem);

        _apply = DialogStyle.Button("Appliquer", primary: true, isDefault: true);
        var cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        _apply.Click += async (_, _) => await ApplyAsync();
        cancel.Click += (_, _) => Close();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Children = { cancel, _apply } };
        var right = new DockPanel { Children = { buttons, form } };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var body = new DockPanel { Children = { left } };
        DockPanel.SetDock(left, Dock.Left);
        body.Children.Add(right);

        var card = DialogStyle.Card(new StackPanel { Children = { DialogStyle.Header(_action.Glyph, System.IO.Path.GetFileName(path), prompt.Question, Close), body } });
        Content = card;
        DialogStyle.Appear(this, card);
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshPreviewAsync();
        };
        Loaded += async (_, _) =>
        {
            _value.Focus();
            _value.SelectAll();
            await RefreshPreviewAsync();
        };
    }

    private string Value => _logo is not null ? WatermarkInput.ImagePrefix + _logo : _value.Text;

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void PickLogo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Image du filigrane", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif" };
        if (dialog.ShowDialog(this) == true)
        {
            SetLogo(dialog.FileName);
        }
    }

    private void SetLogo(string? file)
    {
        _logo = file;
        _logoChip.Visibility = file is null ? Visibility.Collapsed : Visibility.Visible;
        _logoName.Text = file is null ? string.Empty : System.IO.Path.GetFileName(file);
        _value.IsEnabled = file is null;
        Schedule();
    }

    private async Task RefreshPreviewAsync()
    {
        var generation = ++_generation;
        var value = Value;
        _apply.IsEnabled = false;
        _busy.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
        string? temp = null;
        try
        {
            var context = new ActionContext { Input = value };
            temp = await _action.BuildPreviewAsync(_path, context, CancellationToken.None);
            var bitmap = temp is null ? null : await Task.Run(() =>
            {
                using var stream = new MemoryStream(Raster.PdfPageToPng(temp, 0, 90));
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            });
            if (generation != _generation)
            {
                return;
            }

            _image.Source = bitmap;
            _problem.Text = string.Empty;
            _apply.IsEnabled = !string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _problem.Text = ex.Message;
            }
        }
        finally
        {
            if (temp is not null)
            {
                OutputPaths.Discard(temp);
            }

            if (generation == _generation)
            {
                _busy.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private async Task ApplyAsync()
    {
        IsEnabled = false;
        try
        {
            var context = new ActionContext { Input = Value };
            var result = await Task.Run(() => _action.RunAsync([_path], context, null, CancellationToken.None));
            if (result.Errors.Count > 0)
            {
                throw new InvalidOperationException(result.Errors[0].Message);
            }

            var output = result.OutputFiles[0];
            TrayBridge.Notify(_action.Id == "pdf.watermark" ? "Filigrane ajouté" : "PDF recadré",
                $"{System.IO.Path.GetFileName(_path)} → {System.IO.Path.GetFileName(output)}", output);
            Close();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            IsEnabled = true;
            _problem.Text = ex.Message;
        }
    }
}
