using Raf.Core;

namespace Raf.Tests;

public class ScreenRulesTests
{
    private static readonly (int, int, int, int) Monitor = (0, 0, 1920, 1080);
    private static readonly (int, int, int, int) LeftMonitor = (-1920, 0, 0, 1080);

    [Fact]
    public void Ekrani_kaplayan_basliksiz_pencere_tam_ekrandir()
    {
        Assert.True(ScreenRules.IsFullscreen((0, 0, 1920, 1080), Monitor, maximized: false, hasCaption: false));
        Assert.True(ScreenRules.IsFullscreen((-1920, 0, 0, 1080), LeftMonitor, maximized: false, hasCaption: false));
        // Tarayıcıda F11: pencere büyütülmüş sayılmaya devam eder ama başlığı kalkar.
        Assert.True(ScreenRules.IsFullscreen((0, 0, 1920, 1080), Monitor, maximized: true, hasCaption: false));
    }

    [Fact]
    public void Buyutulmus_ya_da_normal_pencere_tam_ekran_sayilmaz()
    {
        // Görev çubuğu görünürken büyütülmüş pencere ekranın altına kadar inmez.
        Assert.False(ScreenRules.IsFullscreen((-8, -8, 1928, 1040), Monitor, maximized: true, hasCaption: true));
        // Görev çubuğu otomatik gizlenirken ekranı kaplar ama başlığı durur.
        Assert.False(ScreenRules.IsFullscreen((-8, -8, 1928, 1088), Monitor, maximized: true, hasCaption: true));
        Assert.False(ScreenRules.IsFullscreen((450, 176, 1470, 856), Monitor, maximized: false, hasCaption: true));
        // Simge durumundaki oyun ekranın dışındadır.
        Assert.False(ScreenRules.IsFullscreen((-32000, -32000, -31840, -31972), Monitor, maximized: false, hasCaption: false));
    }
}
