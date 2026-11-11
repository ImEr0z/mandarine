using System.Windows;
using System.Windows.Controls;
using Mandarine.App.Services;
using Mandarine.Core.Converters;

namespace Mandarine.App.Views;

/// <summary>Small window docked on the right of the screen with the key sentences of a PDF.</summary>
internal sealed class PdfSummaryWindow : Window
{
    private readonly StackPanel _list = new() { Margin = new Thickness(14, 10, 14, 10) };
    private IReadOnlyList<string> _sentences = [];

    public PdfSummaryWindow(string path)
    {
        Title = $"Résumé – {Path.GetFileName(path)}";
        Width = 380;
        Height = 460;
        MinWidth = 300;
        MinHeight = 200;
        WindowStartupLocation = WindowStartupLocation.Manual;
        var work = SystemParameters.WorkArea;
        Left = work.Right - Width - 16;
        Top = work.Top + 60;
        Topmost = true;
        SetResourceReference(BackgroundProperty, "Panel.Background");
        SetResourceReference(ForegroundProperty, "Text.Primary");
        SetResourceReference(FontFamilyProperty, "TextFont");

        var copy = new Button { Content = "Copier", Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0), IsEnabled = false };
        copy.Click += (_, _) =>
        {
            try
            {
                Clipboard.SetText(string.Join(Environment.NewLine + Environment.NewLine, _sentences.Select(s => "• " + s)));
                copy.Content = "Copié";
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                copy.Content = "Réessayer";
            }
        };
        var close = new Button { Content = "Fermer", Padding = new Thickness(14, 5, 14, 5), IsCancel = true };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 8, 12, 10), Children = { copy, close } };

        _list.Children.Add(new TextBlock { Text = "Lecture du document…", Opacity = 0.7 });
        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        Loaded += async (_, _) =>
        {
            try
            {
                _sentences = await Task.Run(() => TextSummary.SummarizePdf(path));
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                _list.Children.Clear();
                _list.Children.Add(new TextBlock { Text = "Lecture impossible : " + ex.Message, TextWrapping = TextWrapping.Wrap });
                return;
            }

            _list.Children.Clear();
            if (_sentences.Count == 0)
            {
                _list.Children.Add(new TextBlock { Text = "Pas assez de texte pour faire un résumé (document scanné ?).", TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
                return;
            }

            foreach (var sentence in _sentences)
            {
                _list.Children.Add(new TextBlock { Text = "• " + sentence, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), LineHeight = 20 });
            }

            copy.IsEnabled = true;
        };
    }
}
