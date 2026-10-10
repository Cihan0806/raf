using System.Windows;
using Microsoft.Win32;

namespace Raf.App;

/// <summary>Windows'un açık/koyu uygulama ayarını izleyip renk sözlüğünü değiştirir.</summary>
internal static class Theme
{
    public static bool IsDark { get; private set; } = true;

    /// <summary>Boş değilse Windows ayarı yerine bu kullanılır (yalnızca geliştirme sırasında, --theme ile).</summary>
    public static bool? Forced { get; set; }

    public static void Apply(Application app)
    {
        IsDark = Forced ?? ReadIsDark();
        var source = new Uri(IsDark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = source };
    }

    private static bool ReadIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }
}
