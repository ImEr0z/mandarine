using System.Windows;
using Microsoft.Win32;

namespace Mandarine.App.Services;

/// <summary>Swaps the light/dark brush dictionary to follow the Windows app theme.</summary>
internal static class ThemeManager
{
    private const string ThemeFolder = "/Mandarine;component/Themes/";

    public static void Apply(Application application)
    {
        var name = IsDarkMode() ? "Dark.xaml" : "Light.xaml";
        var dictionaries = application.Resources.MergedDictionaries;
        var current = dictionaries.FirstOrDefault(d =>
            d.Source?.OriginalString.EndsWith("Light.xaml") == true || d.Source?.OriginalString.EndsWith("Dark.xaml") == true);
        if (current?.Source?.OriginalString.EndsWith(name) == true)
        {
            return;
        }

        var replacement = new ResourceDictionary { Source = new Uri(ThemeFolder + name, UriKind.Relative) };
        if (current is null)
        {
            dictionaries.Insert(0, replacement);
        }
        else
        {
            dictionaries[dictionaries.IndexOf(current)] = replacement;
        }
    }

    public static bool IsDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
