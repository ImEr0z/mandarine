using System.Windows;
using System.Windows.Controls;
using Mandarine.App.Services;
using Mandarine.Core;

namespace Mandarine.App.Views;

/// <summary>Title, author and the other hidden facts of a PDF: edit them, or clear what should not be shared.</summary>
internal sealed class PdfMetadataWindow : Window
{
    private readonly string _path;
    private readonly TextBox _title = new();
    private readonly TextBox _author = new();
    private readonly TextBox _subject = new();
    private readonly TextBox _keywords = new();
    private readonly CheckBox _stripHidden = new() { IsChecked = false };
    private readonly CheckBox _stripBookmarks = new();

    public PdfMetadataWindow(string path)
    {
        _path = path;
        PdfInfo info;
        try
        {
            info = PdfMetadata.Read(path);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Ce PDF ne peut pas être lu (protégé par un mot de passe ?).", ex);
        }

        Title = $"Métadonnées – {System.IO.Path.GetFileName(path)}";
        DialogStyle.Frameless(this, 580);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        foreach (var field in new[] { _title, _author, _subject, _keywords })
        {
            DialogStyle.StyleField(field, 36);
        }

        (_title.Text, _author.Text, _subject.Text, _keywords.Text) = (info.Title, info.Author, info.Subject, info.Keywords);

        var facts = new List<string>();
        if (info.Creator.Trim().Length > 0)
        {
            facts.Add($"Créé avec : {info.Creator.Trim()}");
        }

        if (info.Producer.Trim().Length > 0)
        {
            facts.Add($"Produit par : {info.Producer.Trim()}");
        }

        if (info.Created is { } created)
        {
            facts.Add($"Créé le {created:d MMMM yyyy}");
        }

        if (info.Modified is { } modified)
        {
            facts.Add($"Modifié le {modified:d MMMM yyyy}");
        }

        facts.Add(info.Bookmarks > 0 ? $"{info.Bookmarks} marque-page{(info.Bookmarks > 1 ? "s" : string.Empty)}" : "Aucun marque-page");
        if (info.HasXmp)
        {
            facts.Add("Contient des métadonnées XMP");
        }

        var factsBox = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 14, 0, 0), Child = DialogStyle.Muted(string.Join("  ·  ", facts)) };
        factsBox.SetResourceReference(Border.BackgroundProperty, "Tile.Background");

        _stripHidden.Content = new TextBlock { Text = "Effacer le programme, les dates et les métadonnées d'origine (avant de partager)", TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
        _stripBookmarks.Content = new TextBlock { Text = "Retirer les marque-pages", FontSize = 13.5 };
        _stripHidden.Margin = new Thickness(0, 14, 0, 4);
        _stripHidden.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
        _stripBookmarks.SetResourceReference(Control.ForegroundProperty, "Text.Primary");
        _stripBookmarks.IsEnabled = info.Bookmarks > 0;
        _stripBookmarks.Margin = new Thickness(0, 4, 0, 0);

        var note = DialogStyle.Muted("Le fichier d'origine n'est pas modifié : une copie est créée à côté. La mention « PDFsharp » du producteur ne peut pas être retirée.", 12);
        note.Margin = new Thickness(0, 12, 0, 0);

        var cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        var save = DialogStyle.Button("Enregistrer une copie", primary: true, isDefault: true);
        cancel.Click += (_, _) => Close();
        save.Click += (_, _) => Save();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Children = { cancel, save } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children =
            {
                DialogStyle.Header("", System.IO.Path.GetFileName(path), "Titre, auteur et infos cachées", Close),
                Label("Titre"), _title, Label("Auteur"), _author, Label("Sujet"), _subject, Label("Mots-clés"), _keywords,
                factsBox, _stripHidden, _stripBookmarks, note, buttons,
            },
        });
        Content = card;
        DialogStyle.Appear(this, card);
        Loaded += (_, _) => _title.Focus();
    }

    private static TextBlock Label(string text)
    {
        var label = DialogStyle.Muted(text, 12.5);
        label.Margin = new Thickness(2, 10, 0, 4);
        return label;
    }

    private void Save()
    {
        var strip = _stripHidden.IsChecked == true;
        var output = OutputPaths.ForFile(_path, "pdf", null, strip ? "nettoyé" : "métadonnées");
        try
        {
            PdfMetadata.Save(_path, output, new PdfInfoEdit(_title.Text, _author.Text, _subject.Text, _keywords.Text, strip, _stripBookmarks.IsChecked == true));
        }
        catch (Exception ex)
        {
            OutputPaths.Discard(output);
            ErrorLog.Write(ex);
            MessageBox.Show(this, ex.Message, "Mandarine", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TrayBridge.Notify(strip ? "PDF nettoyé" : "Métadonnées enregistrées",
            $"{System.IO.Path.GetFileName(_path)} → {System.IO.Path.GetFileName(output)}", output);
        Close();
    }
}
