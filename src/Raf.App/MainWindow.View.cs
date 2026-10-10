using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Raf.Core;

namespace Raf.App;

// Görünüm seçenekleri: kutucuk/ayrıntı listesi, sıralama, önizleme bölmesi ve dosya içinde arama.
public partial class MainWindow
{
    private const int MaxContentHits = 60;
    private const int ContentSearchDelayMs = 350;

    // Yeni seçilen sütunun ilk yönü: ad ve tür A'dan Z'ye, tarih ve boyut büyükten küçüğe.
    private static bool DefaultDescending(SortKey key) => key is SortKey.Date or SortKey.Size;

    private CancellationTokenSource? _contentCancel;
    private string? _contentQuery;
    private List<ContentSearch.Hit> _contentHits = new();

    // ===== Kutucuklar / ayrıntı listesi =====

    private void ApplyViewMode()
    {
        bool details = _s.Config.DetailsView;
        ItemsList.ItemsPanel = (ItemsPanelTemplate)FindResource(details ? "RowPanel" : "TilePanel");
        ItemsList.ItemTemplate = (DataTemplate)FindResource(details ? "RowTemplate" : "TileTemplate");
        ItemsList.ItemContainerStyle = (Style)FindResource(details ? "RowItem" : "TileItem");
        // Ayrıntı listesinde kaydırma çubuğunun yeri hep ayrılır; yoksa çubuk belirince sütunlar başlıklardan kayar.
        ScrollViewer.SetVerticalScrollBarVisibility(ItemsList, details ? ScrollBarVisibility.Visible : ScrollBarVisibility.Auto);

        // Düğme, basınca geçilecek görünümü gösterir.
        ViewGlyph.Text = details ? "" : "";
        ViewButton.ToolTip = details ? "Kutucuk görünümüne geç" : "Ayrıntı listesine geç";

        bool preview = _s.Config.ShowPreview;
        Preview.Visibility = preview ? Visibility.Visible : Visibility.Collapsed;
        PreviewGlyph.SetResourceReference(TextBlock.ForegroundProperty, preview ? "AccentBrush" : "MutedBrush");
        PreviewButton.ToolTip = preview ? "Önizleme bölmesini kapat" : "Önizleme bölmesini aç";

        UpdateColumnHeaders();
    }

    private void SetDetailsView(bool details)
    {
        if (_s.Config.DetailsView == details) return;
        _s.Config.DetailsView = details;
        _s.Save();
        ApplyViewMode();
        BuildItems();
    }

    private void SetPreview(bool show)
    {
        if (_s.Config.ShowPreview == show) return;
        _s.Config.ShowPreview = show;
        _s.Save();
        ApplyViewMode();
        UpdatePreview();
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e) => SetDetailsView(!_s.Config.DetailsView);

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => SetPreview(!_s.Config.ShowPreview);

    private void ItemsList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (_s.Config.ShowPreview) Preview.Show(Selected());
    }

    // ===== Sıralama =====

    private void SetSort(SortKey key, bool descending)
    {
        _s.Config.SortBy = key;
        _s.Config.SortDescending = descending;
        _s.Save();
        UpdateColumnHeaders();
        BuildItems();
    }

    private void UpdateColumnHeaders()
    {
        string Label(string text, SortKey key) =>
            _s.Config.SortBy != key ? text : text + (_s.Config.SortDescending ? "  ↓" : "  ↑");

        NameHeader.Content = Label("Ad", SortKey.Name);
        TypeHeader.Content = Label("Tür", SortKey.Type);
        SizeHeader.Content = Label("Boyut", SortKey.Size);
        DateHeader.Content = Label("Eklenme", SortKey.Date);
    }

    private void ColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        var key = Enum.Parse<SortKey>((string)((FrameworkElement)sender).Tag);
        // Aynı sütuna yeniden basmak yönü çevirir.
        SetSort(key, key == _s.Config.SortBy ? !_s.Config.SortDescending : DefaultDescending(key));
    }

    private void SortButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var entry in SortEntries()) menu.Items.Add(entry);
        menu.IsOpen = true;
    }

    /// <summary>Sıralama seçenekleri; hem araç çubuğundaki düğmede hem sağ tık menüsünde kullanılır.</summary>
    private IEnumerable<object> SortEntries()
    {
        var current = _s.Config.SortBy;
        bool descending = _s.Config.SortDescending;

        yield return Choice("Ad", current == SortKey.Name, () => SetSort(SortKey.Name, DefaultDescending(SortKey.Name)));
        yield return Choice("Eklenme tarihi", current == SortKey.Date, () => SetSort(SortKey.Date, DefaultDescending(SortKey.Date)));
        yield return Choice("Boyut", current == SortKey.Size, () => SetSort(SortKey.Size, DefaultDescending(SortKey.Size)));
        yield return Choice("Tür", current == SortKey.Type, () => SetSort(SortKey.Type, DefaultDescending(SortKey.Type)));
        yield return new Separator();
        yield return Choice("Artan", !descending, () => SetSort(current, descending: false));
        yield return Choice("Azalan", descending, () => SetSort(current, descending: true));
    }

    /// <summary>Seçiliyse başında onay işareti duran menü satırı.</summary>
    private MenuItem Choice(string header, bool chosen, Action action) => MenuEntry(header, chosen ? "" : "", action);

    // ===== Dosya içinde arama =====

    /// <summary>
    /// Ada göre sonuçların altına eklenecek "içinde geçenler" grubunu döndürür. Sonuçlar henüz
    /// hazır değilse aramayı arka planda başlatır ve boş döner; arama bitince liste yeniden kurulur.
    /// </summary>
    private IEnumerable<ItemVM> ContentResults(string query, bool skipContent, List<ItemVM> shown, Func<LibraryItem, GroupVM, string, ItemVM> vm)
    {
        if (skipContent || query.Length < ContentSearch.MinQueryLength)
        {
            ForgetContentHits();
            return Enumerable.Empty<ItemVM>();
        }

        var shownPaths = new HashSet<string>(shown.Select(v => v.Item.Path), StringComparer.OrdinalIgnoreCase);
        if (query != _contentQuery)
        {
            StartContentSearch(query, shownPaths);
            return Enumerable.Empty<ItemVM>();
        }

        var hits = _contentHits.Where(h => !shownPaths.Contains(h.Item.Path)).ToList();
        if (hits.Count == 0) return Enumerable.Empty<ItemVM>();

        var group = new GroupVM { Title = $"İçinde “{query}” geçenler", CountText = hits.Count.ToString() };
        return hits.Select(h => vm(h.Item, group, h.Snippet)).ToList();
    }

    private async void StartContentSearch(string query, HashSet<string> alreadyShown)
    {
        _contentCancel?.Cancel();
        var cancel = _contentCancel = new CancellationTokenSource();
        _contentQuery = null;
        _contentHits = new();
        var library = _library;

        try
        {
            // Yazma sürerken her harfte dosyaları okumamak için kısa bir bekleme.
            await Task.Delay(ContentSearchDelayMs, cancel.Token);
            var hits = await Task.Run(
                () => ContentSearch.Find(library.Where(i => !alreadyShown.Contains(i.Path)), query, MaxContentHits, cancel.Token),
                cancel.Token);
            if (cancel.IsCancellationRequested) return;

            _contentQuery = query;
            _contentHits = hits;
            if (hits.Count > 0 && SearchBox.Text.Trim() == query) BuildItems(keepScroll: true);
        }
        catch (OperationCanceledException)
        {
            // Sorgu değişti; yeni arama kendi sonucunu getirecek.
        }
    }

    /// <summary>Kitaplık ya da sorgu değişince eski içerik sonuçları geçersizdir.</summary>
    private void ForgetContentHits()
    {
        _contentCancel?.Cancel();
        _contentQuery = null;
        _contentHits = new();
    }
}
