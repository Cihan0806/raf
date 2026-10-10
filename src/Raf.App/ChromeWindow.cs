using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Raf.App;

/// <summary>
/// Windows'un başlık çubuğu yerine kendi içeriğini çizen pencere tabanı.
/// Gölge, yuvarlak köşe ve kenardan boyutlandırma Windows'ta kalır.
/// </summary>
public class ChromeWindow : Window
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    /// <summary>Pencerenin üstünde, tutup sürüklenebilen şeridin yüksekliği.</summary>
    protected virtual double CaptionHeight => 40;

    public ChromeWindow()
    {
        SetResourceReference(BackgroundProperty, "BgBrush");
        SetResourceReference(BorderBrushProperty, "BgBrush");
        SetResourceReference(ForegroundProperty, "TextBrush");
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13.5;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = CaptionHeight,
            ResizeBorderThickness = new Thickness(6),
            // Sıfır olursa Windows pencereye gölge çizmez.
            GlassFrameThickness = new Thickness(0, 0, 0, 1),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = Theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
        int corner = DwmCornerRound;
        DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref corner, sizeof(int));
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        // Başlık çubuğu kaldırılmış pencere ekranı kaplayınca kenarları ekran dışına taşar; taşan payı içeri al.
        BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
