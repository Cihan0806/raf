using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Raf.Core;
using WinForms = System.Windows.Forms;

namespace Raf.App.Views;

/// <summary>
/// Kısayol tuşuyla ekranın ortasında açılan küçük arama çubuğu: yaz, Enter'la aç, Esc ile kapat.
/// Kategoriler, kurallar ve ayarlar tam pencerededir; çubuk yalnızca bulup açmak içindir.
/// </summary>
public partial class QuickBar : Window
{
    private const int MaxResults = 30;

    /// <summary>Çubuğun, ekranın çalışma alanının üstünden ne kadar aşağıda duracağı (oran).</summary>
    private const double TopRatio = 0.2;

    /// <summary>Aramada Raf'ın kendisinin eşleştiği ad (<see cref="TextUtil.Fold"/> ile sadeleştirilmiş).</summary>
    private const string SelfName = "raf";

    private const string IdleHint = "Uygulama, dosya ya da uzantı yaz     ·     Enter: tam pencere     ·     Esc: kapat";
    private const string NoResultHint = "Esc: kapat";
    private const string ResultHint = "Enter: aç     ·     Ctrl+Enter: konumunu göster     ·     ↑ ↓: seç     ·     Esc: kapat";

    /// <summary>Henüz bir kategoriye girmemiş, Gelen'de bekleyen öğelerin simgesinin kodu (indirme oku).</summary>
    private const string InboxIcon = "E896";

    // Eşit derecede iyi eşleşen sonuçlarda kimin önce geleceği: çubuk en çok uygulama açmak için kullanılır.
    private const int SourceSelf = -1;
    private const int SourceApp = 0;
    private const int SourceRaf = 1;
    private const int SourcePlace = 2;
    private const int SourceDesktop = 3;

    // Pencerenin kenarındaki gölge payı; QuickBar.xaml'deki Grid.Margin ile aynı olmalı.
    private const double ShadowLeft = 26;
    private const double ShadowTop = 18;

    /// <summary>Çubuğun ekran kenarına en fazla ne kadar yaklaşacağı.</summary>
    private const double ScreenGap = 8;

    // İki kip: kısayol tuşuyla ekranın ortasında açılan geniş çubuk ve arama kutucuğuna tıklanınca
    // kutucuğun olduğu yerde açılan dar kutu. Genişlikler pencerenin tamamıdır (iki yandaki gölge payı dahil).
    private const double WideWidth = 700;
    private const double CompactWidth = 452;
    private const double WideRowHeight = 58;

    /// <summary>Kutucuğun yüksekliğiyle aynı: kutucuk açılırken yalnızca yana doğru büyümüş görünür.</summary>
    private const double CompactRowHeight = 42;

    private static readonly Duration ExpandDuration = TimeSpan.FromMilliseconds(190);
    private static readonly Duration CollapseDuration = TimeSpan.FromMilliseconds(130);

    /// <summary>Sonuç listesi tam açıkken pencerenin yüksekliği (yaklaşık); ekrandan taşmasın diye kullanılır.</summary>
    private const double WideFullHeight = 540;
    private const double CompactFullHeight = 440;

    /// <summary>Çubuk her açıldığında uygulama listesi bundan eskiyse arka planda yenilenir.</summary>
    private static readonly TimeSpan LaunchablesMaxAge = TimeSpan.FromSeconds(45);

    private static readonly GroupVM NoGroup = new();

    private readonly Services _s = App.S;
    private readonly Action _openFullWindow;
    private readonly Action _openDefaultView;
    private List<LibraryItem> _library = new();
    private Dictionary<string, DateTime> _added = new(StringComparer.OrdinalIgnoreCase);
    private int _scanVersion;
    private bool _compact;
    private bool _collapsing;

    /// <summary>Dar kipte kutunun açılırken başladığı, kapanırken döndüğü genişlik (kutucuğun genişliği).</summary>
    private double _anchorWidth;
    private Action? _onHidden;

    /// <param name="openFullWindow">Tam pencereyi, kaldığı yerden açar.</param>
    /// <param name="openDefaultView">Tam pencereyi varsayılan sekmesiyle açar.</param>
    public QuickBar(Action openFullWindow, Action openDefaultView)
    {
        InitializeComponent();
        _openFullWindow = openFullWindow;
        _openDefaultView = openDefaultView;
        Hint.Text = IdleHint;

        // Pencere tanıtıcısı ilk gösterimden önce de olsun ki ekran ölçeği okunabilsin.
        new WindowInteropHelper(this).EnsureHandle();

        _s.Sorter.Moved += _ => Dispatcher.InvokeAsync(RescanIfVisible);
        _s.Changed += () => Dispatcher.InvokeAsync(RescanIfVisible);
        _s.Launchables.Updated += () => Dispatcher.InvokeAsync(() =>
        {
            if (IsVisible) UpdateResults();
        });
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) return;
            var callback = _onHidden;
            _onHidden = null;
            callback?.Invoke();
        };
    }

    /// <summary>Çubuğun boyutlarını kipe göre ayarlar; dar kip arama kutucuğunun büyümüş hâli gibi görünür.</summary>
    private void ApplyMode(bool compact)
    {
        _compact = compact;
        _collapsing = false;
        Width = compact ? CompactWidth : WideWidth;

        // Dar kipte kutunun genişliği açıkça verilir ki açılıp kapanırken oynatılabilsin; geniş kipte pencereyi doldurur.
        Box.BeginAnimation(WidthProperty, null);
        Shadow.BeginAnimation(WidthProperty, null);
        Box.Width = Shadow.Width = compact ? CompactWidth - 2 * ShadowLeft : double.NaN;
        Box.HorizontalAlignment = Shadow.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;

        // Dar kipte köşeler arama satırının yarısı kadardır: sonuç yokken kutu, kutucuk gibi hap biçimindedir.
        var radius = new CornerRadius(compact ? CompactRowHeight / 2 : 14);
        Shadow.CornerRadius = radius;
        Box.CornerRadius = radius;

        SearchRow.Height = compact ? CompactRowHeight : WideRowHeight;
        GlyphColumn.Width = new GridLength(compact ? 46 : 54);
        SearchGlyph.FontSize = compact ? 15 : 17;
        SearchBox.FontSize = compact ? 15.5 : 19;
        SearchBox.Tag = compact ? "Uygulama ara" : "Ara: uygulama, dosya, klasör";
        Footer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        Results.MaxHeight = compact ? 288 : 352;
    }

    public void Toggle()
    {
        if (IsVisible) Dismiss();
        else ShowBar();
    }

    /// <summary>
    /// Kutunun genişliğini oynatır. Windows'ta "pencere içi animasyonlar" kapalıysa doğrudan son değere geçer.
    /// </summary>
    private void AnimateWidth(double from, double to, Duration duration, EasingMode easing, Action? completed = null)
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            Box.Width = Shadow.Width = to;
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(from, to, duration) { EasingFunction = new CubicEase { EasingMode = easing } };
        Shadow.BeginAnimation(WidthProperty, animation.Clone());
        if (completed is not null) animation.Completed += (_, _) => completed();
        Box.BeginAnimation(WidthProperty, animation);
    }

    /// <summary>
    /// Çubuğu kullanıcı kapattığında (Esc, başka yere tıklama) çağrılır. Dar kipte kutu önce kutucuğun
    /// genişliğine geri daralır, sonra gizlenir; kutucuk kaldığı yerden görünür.
    /// </summary>
    private void Dismiss()
    {
        if (!IsVisible || _collapsing) return;
        if (!_compact)
        {
            Hide();
            return;
        }

        _collapsing = true;
        ResultsPanel.Visibility = Visibility.Collapsed;
        AnimateWidth(Box.ActualWidth, _anchorWidth, CollapseDuration, EasingMode.EaseIn, () =>
        {
            if (!_collapsing) return;
            _collapsing = false;
            Hide();
        });
    }

    public void ShowBar() => ShowBar(anchor: null, onHidden: null);

    /// <param name="anchor">
    /// Verilirse (arama kutucuğunun ekrandaki dikdörtgeni) arama dar kipte, o dikdörtgenin ortasına
    /// hizalı açılır: kutucuk olduğu yerde büyümüş gibi görünür. Verilmezse geniş çubuk, fare imlecinin
    /// bulunduğu ekranın ortasında açılır.
    /// </param>
    /// <param name="onHidden">Çubuk kapanınca bir kez çağrılır.</param>
    public void ShowBar(Rect? anchor, Action? onHidden)
    {
        _onHidden = onHidden;
        SearchBox.Clear();
        ApplyMode(compact: anchor is not null);
        if (anchor is { } rect)
        {
            PlaceOver(rect);
            _anchorWidth = rect.Width;
            AnimateWidth(rect.Width, CompactWidth - 2 * ShadowLeft, ExpandDuration, EasingMode.EaseOut);
        }
        else
        {
            PlaceOnCursorScreen();
        }
        Show();
        Activate();
        SearchBox.Focus();
        Rescan();
        _ = _s.Launchables.RefreshAsync(LaunchablesMaxAge);
    }

    /// <summary>Çubuğu, fare imlecinin bulunduğu ekranda yatayda ortalar.</summary>
    private void PlaceOnCursorScreen()
    {
        var area = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double areaLeft = area.Left / dpi.DpiScaleX;
        double areaTop = area.Top / dpi.DpiScaleY;
        double areaWidth = area.Width / dpi.DpiScaleX;
        double areaHeight = area.Height / dpi.DpiScaleY;

        Left = areaLeft + (areaWidth - Width) / 2;
        Top = areaTop + areaHeight * TopRatio;
    }

    /// <summary>
    /// Dar kutuyu verilen dikdörtgenin (arama kutucuğunun) tam ortasına hizalar: yatayda iki yana eşit
    /// büyür, arama satırı kutucukla aynı hizada kalır. Ekrandan taşacaksa içeri çekilir.
    /// </summary>
    private void PlaceOver(Rect anchor)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point(
            (int)((anchor.Left + anchor.Width / 2) * dpi.DpiScaleX),
            (int)((anchor.Top + anchor.Height / 2) * dpi.DpiScaleY));
        var area = WinForms.Screen.FromPoint(center).WorkingArea;
        double areaLeft = area.Left / dpi.DpiScaleX;
        double areaTop = area.Top / dpi.DpiScaleY;
        double areaRight = area.Right / dpi.DpiScaleX;
        double areaBottom = area.Bottom / dpi.DpiScaleY;

        double left = anchor.Left + anchor.Width / 2 - Width / 2;
        double top = anchor.Top + anchor.Height / 2 - SearchRow.Height / 2 - ShadowTop;
        Left = Math.Max(areaLeft - ShadowLeft + ScreenGap, Math.Min(left, areaRight - Width + ShadowLeft - ScreenGap));
        Top = Math.Max(areaTop - ShadowTop + ScreenGap, Math.Min(top, areaBottom - CompactFullHeight));
    }

    /// <summary>Uygulamadan çıkılırken true yapılır; o zamana kadar kapatma isteği çubuğu yalnızca gizler.</summary>
    public bool Exiting { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Exiting)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    // ===== Veri =====

    private void RescanIfVisible()
    {
        if (IsVisible) Rescan();
    }

    private async void Rescan()
    {
        int version = ++_scanVersion;
        var (items, added) = await _s.ScanAsync();
        if (version != _scanVersion) return;

        _library = items;
        _added = added;
        UpdateResults();
    }

    private DateTime AddedOf(LibraryItem item) =>
        _added.TryGetValue(item.Path, out var time) ? time : item.Added;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (IsInitialized) UpdateResults();
    }

    private void UpdateResults()
    {
        var query = SearchBox.Text.Trim();
        if (query.Length == 0)
        {
            Results.ItemsSource = null;
            ResultsPanel.Visibility = Visibility.Collapsed;
            Hint.Text = IdleHint;
            return;
        }

        Dictionary<string, Category> categories;
        Dictionary<string, string> pathNames;
        lock (_s.Config)
        {
            categories = _s.Config.Categories.ToDictionary(c => c.Id);
            pathNames = _s.Config.Categories.ToDictionary(c => c.Id, c => _s.Config.PathName(c));
        }

        object RowOf(LibraryItem item)
        {
            categories.TryGetValue(item.CategoryId, out var category);
            var name = item.CategoryId == Library.InboxId ? "Gelen" : pathNames.GetValueOrDefault(item.CategoryId, "");
            return new ItemVM(item, NoGroup, name, AddedOf(item))
            {
                CategoryGlyph = Ui.Glyph(category is null ? InboxIcon : category.Icon),
                CategoryDot = Ui.BrushOf(category?.Color),
            };
        }

        // Yalnızca uzantı yazıldıysa Raf'taki o türden dosyalar en başta gelir.
        var tokens = TextUtil.Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var found = LibrarySearch.Find(_library, query, AddedOf, MaxResults);
        var rows = found.ByExtension.Select(RowOf).ToList();

        // Geri kalanı tek listedir: Raf'takiler, uygulamalar, sistem yerleri ve masaüstündekiler; en iyi eşleşen önce.
        var inRaf = new HashSet<string>(_library.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        // Raf'a taşınmış bir kısayol ("Brave.lnk") ile uygulamalar listesindeki "Brave" aynı şeydir; Raf'taki gösterilir.
        var shortcutsInRaf = new HashSet<string>(_library
            .Where(i => !i.IsDirectory && Path.GetExtension(i.Name).ToLowerInvariant() is ".lnk" or ".url")
            .Select(i => TextUtil.Fold(Path.GetFileNameWithoutExtension(i.Name))));
        var candidates = new List<(int Score, int Source, string Name, Func<object> Row)>();
        foreach (var item in found.ByName)
        {
            int score = TextUtil.MatchScore(TextUtil.Fold(item.Name), tokens);
            candidates.Add((score < 0 ? 2 : score, SourceRaf, item.Name, () => RowOf(item)));
        }
        // Raf'ın kendisi: seçilince tam pencere varsayılan sekmesiyle açılır.
        int selfScore = TextUtil.MatchScore(SelfName, tokens);
        if (selfScore >= 0) candidates.Add((selfScore, SourceSelf, "Raf", () => new ActionVM("Raf", DefaultViewLabel(), _openDefaultView)));

        foreach (var launchable in _s.Launchables.Items)
        {
            int score = TextUtil.MatchScore(launchable.FoldedName, tokens);
            if (score < 0) continue;
            // Başlat menüsündeki "Raf" kısayolu yukarıdaki satırla aynı şeydir.
            if (launchable.Kind == LaunchKind.App && launchable.FoldedName == SelfName) continue;
            // Raf'a bağlantı olarak eklenmiş bir masaüstü öğesi zaten Raf sonucu olarak çıkıyor.
            if (launchable.Kind == LaunchKind.Desktop && inRaf.Contains(launchable.Target)) continue;
            if (launchable.Kind == LaunchKind.App && shortcutsInRaf.Contains(launchable.FoldedName)) continue;

            int source = launchable.Kind switch
            {
                LaunchKind.App => SourceApp,
                LaunchKind.Place => SourcePlace,
                _ => SourceDesktop,
            };
            candidates.Add((score, source, launchable.Name, () => new LaunchVM(launchable)));
        }
        rows.AddRange(candidates
            .OrderBy(c => c.Score)
            .ThenBy(c => c.Source)
            .ThenBy(c => c.Name, NaturalComparer.Instance)
            .Take(Math.Max(0, MaxResults - rows.Count))
            .Select(c => c.Row()));

        if (rows.Count == 0) Caption.Text = $"“{query}” ile eşleşen bir şey yok.";
        else if (found.ByExtension.Count > 0) Caption.Text = $"Raf'ta “{found.Extension}” türünde {found.ByExtension.Count} dosya";
        else Caption.Text = "";

        Caption.Visibility = Caption.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Caption.Margin = rows.Count == 0 ? new Thickness(20, 12, 20, 12) : new Thickness(20, 11, 20, 3);
        Results.ItemsSource = rows;
        Results.SelectedIndex = rows.Count > 0 ? 0 : -1;
        Results.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultsPanel.Visibility = Visibility.Visible;
        Hint.Text = rows.Count > 0 ? ResultHint : NoResultHint;
        // Dar kipte liste boşsa başlık yazısının altında boşluk kalmasın.
        if (_compact && rows.Count == 0) Caption.Margin = new Thickness(20, 10, 20, 14);
    }

    /// <summary>"Raf" satırının sağında yazan, hangi sekmenin açılacağını söyleyen metin.</summary>
    private string DefaultViewLabel()
    {
        var key = _s.Config.DefaultView;
        if (key == NavKeys.All) return "Tümü sekmesi";
        if (key == NavKeys.Recent) return "Son eklenenler sekmesi";
        if (key == Library.InboxId) return "Gelen'de bekleyen sekmesi";
        if (key.StartsWith(NavKeys.CategoryPrefix, StringComparison.Ordinal))
        {
            lock (_s.Config)
            {
                var category = _s.Config.Find(key[NavKeys.CategoryPrefix.Length..]);
                if (category is not null) return _s.Config.PathName(category) + " sekmesi";
            }
        }
        return "Tam pencere";
    }

    // ===== Klavye ve fare =====

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Dismiss();
                break;
            case Key.Down:
                MoveSelection(+1);
                break;
            case Key.Up:
                MoveSelection(-1);
                break;
            case Key.Enter:
                if (SearchBox.Text.Trim().Length == 0) OpenFullWindow();
                else OpenSelected(reveal: Keyboard.Modifiers == ModifierKeys.Control);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void MoveSelection(int delta)
    {
        int count = Results.Items.Count;
        if (count == 0) return;

        Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + delta, 0, count - 1);
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void Results_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Ui.Ancestor<ListBoxItem>(e.OriginalSource)?.DataContext is not { } row) return;
        Results.SelectedItem = row;
        OpenSelected(reveal: false);
    }

    private void OpenSelected(bool reveal)
    {
        if (Results.SelectedItem is ActionVM action)
        {
            Hide();
            action.Run();
            return;
        }

        string name;
        string target;
        string? location;
        switch (Results.SelectedItem)
        {
            case ItemVM vm:
                (name, target, location) = (vm.Item.Name, vm.Item.Path, vm.Item.Path);
                break;
            case LaunchVM vm:
                // Uygulamaların ve sistem yerlerinin Dosya Gezgini'nde gösterilecek bir konumu yoktur.
                (name, target, location) = (vm.Name, vm.Item.Target, vm.Item.Kind == LaunchKind.Desktop ? vm.Item.Target : null);
                break;
            default:
                return;
        }
        if (reveal && location is null) return;

        Hide();
        try
        {
            if (reveal) Shell.Reveal(location!);
            else Shell.Open(target);
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            MessageDialog.Info(null, "Açılamadı", $"{name}: {e.Message}");
        }
    }

    private void OpenFull_Click(object sender, RoutedEventArgs e) => OpenFullWindow();

    private void OpenFullWindow()
    {
        Hide();
        _openFullWindow();
    }

    /// <summary>Başka bir yere tıklanınca çubuk kendiliğinden kapanır.</summary>
    private void Window_Deactivated(object? sender, EventArgs e) => Dismiss();

#if DEBUG
    /// <summary>Geliştirme yardımcısı: ekran görüntüsü için çubuğu öne geçirmeden, verilen sorguyla gösterir.</summary>
    internal async Task DebugShow(string query, bool compact = false)
    {
        ShowActivated = false;
        ApplyMode(compact);
        PlaceOnCursorScreen();
        Show();
        (_library, _added) = await _s.ScanAsync();
        await _s.Launchables.RefreshAsync(TimeSpan.FromMinutes(10));
        SearchBox.Text = query;
        UpdateResults();
    }
#endif
}
