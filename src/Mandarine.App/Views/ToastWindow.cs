using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Mandarine.App.Services;

namespace Mandarine.App.Views;

/// <summary>
/// The end-of-work notice: a small bubble in the bottom right corner, with "Ouvrir" and "Annuler" buttons.
/// It never takes the focus, stays a few seconds (longer while the mouse is over it), and replaces the previous one.
/// </summary>
internal sealed class ToastWindow : Window
{
    private static ToastWindow? _current;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(8) };
    private readonly Border _card;
    private readonly StackPanel _body = new();
    private bool _closing;

    private ToastWindow(string title, string text, string? openPath, bool canUndo)
    {
        Title = "Mandarine";
        DialogStyle.Frameless(this, 392);
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = false;

        _card = DialogStyle.Card(_body);
        _card.Margin = new Thickness(16);
        _card.Padding = new Thickness(16, 14, 16, 14);
        Fill(title, text, openPath, canUndo);
        Content = _card;

        _timer.Tick += (_, _) => Dismiss();
        MouseEnter += (_, _) => _timer.Stop();
        MouseLeave += (_, _) =>
        {
            _timer.Stop();
            _timer.Start();
        };
        Loaded += (_, _) =>
        {
            var work = SystemParameters.WorkArea;
            Left = work.Right - ActualWidth;
            Top = work.Bottom - ActualHeight;
            var duration = TimeSpan.FromMilliseconds(180);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _card.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
            var scale = (ScaleTransform)_card.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = ease });
            _timer.Start();
        };
    }

    /// <summary>Shows the bubble. <paramref name="canUndo"/> adds the "Annuler" button (puts the last conversion in the Recycle Bin).</summary>
    public static void ShowToast(string title, string text, string? openPath, bool canUndo)
    {
        _current?.Close();
        _current = new ToastWindow(title, text, openPath, canUndo);
        _current.Closed += (_, _) => _current = null;
        _current.Show();
    }

    private void Fill(string title, string text, string? openPath, bool canUndo)
    {
        _body.Children.Clear();
        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Child = new TextBlock { Text = "", FontFamily = DialogStyle.IconFont, FontSize = 15, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        icon.SetResourceReference(Border.BackgroundProperty, "Accent");

        var cross = new TextBlock { Text = "", FontFamily = DialogStyle.IconFont, FontSize = 11, Padding = new Thickness(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Top };
        cross.SetResourceReference(TextBlock.ForegroundProperty, "Text.Secondary");
        cross.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Dismiss();
        };

        var detail = DialogStyle.Muted(text, 12.5);
        detail.MaxHeight = 56;
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        var titles = new StackPanel
        {
            Margin = new Thickness(12, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, detail },
        };
        var head = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(cross, Dock.Right);
        head.Children.Add(icon);
        head.Children.Add(cross);
        head.Children.Add(titles);
        _body.Children.Add(head);

        if (openPath is null && !canUndo)
        {
            return;
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (canUndo)
        {
            var undo = DialogStyle.Button("Annuler", primary: false);
            undo.Click += (_, _) => Undo();
            buttons.Children.Add(undo);
        }

        if (openPath is not null)
        {
            var open = DialogStyle.Button("Ouvrir", primary: true);
            open.Click += (_, _) =>
            {
                if (File.Exists(openPath) || Directory.Exists(openPath))
                {
                    Process.Start(new ProcessStartInfo(openPath) { UseShellExecute = true })?.Dispose();
                }

                Dismiss();
            };
            buttons.Children.Add(open);
        }

        _body.Children.Add(buttons);
    }

    private void Undo()
    {
        var removed = LastConversion.Undo();
        Fill("Conversion annulée", removed == 0 ? "Aucun fichier à supprimer." : $"{removed} élément{(removed > 1 ? "s" : string.Empty)} placé{(removed > 1 ? "s" : string.Empty)} dans la corbeille.", null, canUndo: false);
        _timer.Stop();
        _timer.Interval = TimeSpan.FromSeconds(3);
        _timer.Start();
    }

    private void Dismiss()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
        fade.Completed += (_, _) => Close();
        _card.BeginAnimation(OpacityProperty, fade);
    }
}
