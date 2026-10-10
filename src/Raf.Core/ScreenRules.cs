namespace Raf.Core;

public static class ScreenRules
{
    /// <summary>
    /// Bir pencere tam ekran mı (oyun, tam ekran video)? Bulunduğu ekranın tamamını kaplamalı ve
    /// sıradan "ekranı kapla" hâlinde olmamalıdır: görev çubuğu otomatik gizlenirken büyütülmüş
    /// normal pencereler de ekranı kaplar ama başlık çubukları durur.
    /// </summary>
    public static bool IsFullscreen(
        (int Left, int Top, int Right, int Bottom) window,
        (int Left, int Top, int Right, int Bottom) monitor,
        bool maximized,
        bool hasCaption)
    {
        bool covers = window.Left <= monitor.Left && window.Top <= monitor.Top
                   && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
        return covers && (!maximized || !hasCaption);
    }
}
