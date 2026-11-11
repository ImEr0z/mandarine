using System.Windows;
using System.Windows.Controls;
using Mandarine.Core.Actions;

namespace Mandarine.App.Views;

/// <summary>Asks for one value (pages, text, password) before an action runs.</summary>
internal sealed class InputDialog : Window
{
    private readonly Control _field;

    private InputDialog(string actionTitle, ActionPrompt prompt, string glyph)
    {
        Title = actionTitle;
        DialogStyle.Frameless(this, 440);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;

        _field = prompt.Secret ? new PasswordBox() : new TextBox();
        DialogStyle.StyleField(_field);

        var ok = DialogStyle.Button("Valider", primary: true, isDefault: true);
        ok.IsEnabled = false;
        var cancel = DialogStyle.Button("Annuler", primary: false, isCancel: true);
        ok.Click += (_, _) => DialogResult = true;
        if (_field is TextBox box)
        {
            box.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        }
        else if (_field is PasswordBox password)
        {
            password.PasswordChanged += (_, _) => ok.IsEnabled = password.Password.Length > 0;
        }

        var hint = DialogStyle.Muted(prompt.Hint);
        hint.Margin = new Thickness(2, 8, 0, 20);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, ok } };

        var card = DialogStyle.Card(new StackPanel
        {
            Children = { DialogStyle.Header(glyph, actionTitle, prompt.Question, () => DialogResult = false), _field, hint, buttons },
        });
        Content = card;
        DialogStyle.Appear(this, card);
        Loaded += (_, _) => _field.Focus();
    }

    /// <summary>Returns what the user typed, or null when they cancelled.</summary>
    public static string? Ask(string actionTitle, ActionPrompt prompt, string glyph)
    {
        var dialog = new InputDialog(actionTitle, prompt, glyph);
        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var text = dialog._field is PasswordBox password ? password.Password : ((TextBox)dialog._field).Text;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
