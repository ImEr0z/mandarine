using System.Windows;
using System.Windows.Controls;

namespace Mandarine.App.Views;

/// <summary>Small box shown while a new version downloads at launch; Mandarine restarts into it afterwards.</summary>
internal sealed class UpdateWindow : Window
{
    private readonly ProgressBar _progress = new() { Height = 4, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 12, 0, 0), BorderThickness = new Thickness(0) };

    public UpdateWindow(string version)
    {
        Title = "Mandarine";
        Width = 320;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;
        BorderThickness = new Thickness(1);
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(BorderBrushProperty, "Panel.Border");
        SetResourceReference(FontFamilyProperty, "TextFont");
        _progress.SetResourceReference(ForegroundProperty, "Accent");
        _progress.SetResourceReference(BackgroundProperty, "Tile.Hover");

        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = $"Mise à jour de Mandarine ({version})…", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_progress);
        Content = panel;
    }

    /// <summary>Called from the download thread.</summary>
    public void SetProgress(int percent) => Dispatcher.BeginInvoke(() => _progress.Value = percent);
}
