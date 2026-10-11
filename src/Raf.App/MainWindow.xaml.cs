using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Raf.Core;

namespace Raf.App;

/// <summary>
/// Tam pencere. Bu dosya pencerenin iskeletidir: gösterme/gizleme, veriyi yükleme, kenar çubuğu,
/// öğe listesini kurma ve klavye. Öğe işlemleri MainWindow.Items.cs, sürükle-bırak
/// MainWindow.DragDrop.cs, görünüm/sıralama/önizleme/içerik araması MainWindow.View.cs içindedir.
/// </summary>
public partial class MainWindow : ChromeWindow
{
    private const string AllKey = NavKeys.All;
    private const string RecentKey = NavKeys.Recent;
    private const string CategoryPrefix = NavKeys.CategoryPrefix;
    private const string InboxGlyph = "";

    /// <summary>"Tümü" görünümünde her kategoriden gösterilen öğe sayısı (kabaca iki satır).</summary>
    private const int PreviewPerCategory = 14;
    private const int RecentCount = 42;
    private const int MaxShown = 800;

    private enum Page { Home, Categories, History, Settings }

    /// <summary>Bir kategorinin, listeyi kurarken gereken bilgileri (ayarlar kilidi altında bir kez okunur).</summary>
    private sealed record CategoryInfo(Category Category, string PathName, bool IsChild, List<Category> Children);

    private readonly Services _s = App.S;
    private readonly DispatcherTimer _reloadTimer;
    private readonly DispatcherTimer _snackTimer;
    private readonly DispatcherTimer _notifyTimer;

    private FileSystemWatcher? _rootWatcher;
    private List<LibraryItem> _library = new();
    private Dictionary<string, DateTime> _addedByPath = new(StringComparer.OrdinalIgnoreCase);
    private Page _page = Page.Home;
    private string _navKey = AllKey;
    private bool _buildingNav;
    private bool _dirty = true;
    private bool _exiting;
    private int _reloadVersion;
    private int _reloadQueued;
    private bool _inboxPinned;

    protected override double CaptionHeight => 52;

    public MainWindow()
    {
        InitializeComponent();

        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(keepScroll: true); };
        _snackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(9) };
        _snackTimer.Tick += (_, _) => HideSnack();
        _notifyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _notifyTimer.Tick += (_, _) => FlushNotifications();

        _s.Sorter.Moved += record => Dispatcher.InvokeAsync(() => OnMoved(record));
        _s.Linked += (path, category) => Dispatcher.InvokeAsync(() => OnLinked(path, category));
        _s.Changed += ScheduleReload;
        ItemsList.ContextMenu = new ContextMenu();
        Nav.ContextMenu = new ContextMenu();
        Nav.ContextMenuOpening += Nav_ContextMenuOpening;
        Preview.OpenRequested += vm => OpenItems(new[] { vm });
        Preview.RevealRequested += vm => Shell.Reveal(vm.Item.Path);

        ApplyViewMode();
        WatchRoot();
        InitDiskBar();
    }

    // ===== Göster / gizle =====

    /// <param name="activate">false ise pencere öne geçmeden, o an çalışılan uygulamayı bölmeden açılır.</param>
    /// <param name="toDefault">true ise pencere zaten açık olsa bile varsayılan sekmeye geçilir.</param>
    public void ShowLauncher(bool activate, bool toDefault = false)
    {
        bool wasHidden = !IsVisible;
        ShowActivated = activate;
        if (wasHidden) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (activate)
        {
            Activate();
            // Başka bir uygulama öndeyken çağrıldığında pencere arkada kalmasın.
            Topmost = true;
            Topmost = false;
        }

        // Gizliyken açılan pencere her seferinde varsayılan sekmeden başlar.
        if (activate && (wasHidden || toDefault))
        {
            if (SearchBox.Text.Length > 0) SearchBox.Clear();
            GoHome(DefaultKey());
        }

        if (activate && _page == Page.Home)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
        if (_dirty) Reload(keepScroll: false);
    }

    public void ShowLauncher() => ShowLauncher(activate: true);

    /// <summary>Pencereyi açar ve varsayılan sekmeye geçer (aramadaki "Raf" satırı bunu çağırır).</summary>
    public void ShowDefaultView() => ShowLauncher(activate: true, toDefault: true);

    /// <summary>Raf açılınca gelecek sekme; seçilmemişse "Tümü".</summary>
    private string DefaultKey() => string.IsNullOrEmpty(_s.Config.DefaultView) ? AllKey : _s.Config.DefaultView;

    private void Nav_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Ui.Ancestor<ListBoxItem>(e.OriginalSource)?.DataContext is not NavVM { IsHeader: false } row)
        {
            e.Handled = true;
            return;
        }

        var menu = Nav.ContextMenu!;
        menu.Items.Clear();
        menu.Items.Add(row.IsDefault
            ? MenuEntry("Varsayılanı kaldır", Ui.Glyph("E77A"), () => SetDefaultView("", row.Title))
            : MenuEntry("Varsayılan yap", Ui.Glyph("E718"), () => SetDefaultView(row.Key, row.Title)));
    }

    /// <summary>Raf açılınca gelecek sekmeyi seçer; boş anahtar seçimi kaldırır ("Tümü"ne döner).</summary>
    private void SetDefaultView(string key, string title)
    {
        _s.Config.DefaultView = key;
        _s.Save();
        BuildNav();
        BuildItems(keepScroll: true);
        ShowSnack(key.Length > 0
            ? $"Raf artık “{title}” sekmesiyle açılacak."
            : "Varsayılan sekme kaldırıldı; Raf “Tümü” ile açılacak.", null);
    }

    public void HideLauncher() => Hide();

    /// <summary>Uygulamadan çıkılırken çağrılır; bundan sonra kapatma isteği pencereyi gizlemek yerine kapatır.</summary>
    public void PrepareExit()
    {
        _exiting = true;
        _rootWatcher?.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Alt+F4 ya da görev çubuğundan kapatma: uygulama tepside çalışmayı sürdürür.
        if (!_exiting)
        {
            e.Cancel = true;
            HideLauncher();
        }
        base.OnClosing(e);
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => HideLauncher();

#if DEBUG
    /// <summary>Geliştirme yardımcısı: ekran görüntüsü almak için belirli bir görünüme geçer.</summary>
    internal void DebugGo(string where)
    {
        if (where.StartsWith("search:", StringComparison.Ordinal)) SearchBox.Text = where["search:".Length..];
        else if (where == "first-category") GoHome((Nav.ItemsSource as List<NavVM>)?.First(n => n.Category is not null).Key);
        else if (where == "all") GoHome(AllKey);
        else if (where == "categories") CategoriesRadio.IsChecked = true;
        else if (where == "history") HistoryRadio.IsChecked = true;
        else if (where == "settings") SettingsRadio.IsChecked = true;
        else if (where is "details" or "tiles") SetDetailsView(where == "details");
        else if (where is "preview" or "no-preview") SetPreview(where == "preview");
        else if (where == "snack-undo") SnackUndo_Click(this, new RoutedEventArgs());
        else if (where.StartsWith("select:", StringComparison.Ordinal))
        {
            var name = where["select:".Length..];
            ItemsList.SelectedItem = ItemsList.Items.Cast<ItemVM>().FirstOrDefault(v => v.Item.Name == name);
        }
    }
#endif

    // ===== Veriyi yükleme =====

    private void WatchRoot()
    {
        var root = _s.Config.Root;
        _rootWatcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
        };
        FileSystemEventHandler onChange = (_, e) =>
        {
            // Listeyi en derinde "Kategori\Alt kategori\öğe" etkiler; öğe klasörlerinin içi görünmez.
            var relative = Path.GetRelativePath(root, e.FullPath);
            if (relative.Count(c => c == Path.DirectorySeparatorChar) > 2) return;
            if (Interlocked.Exchange(ref _reloadQueued, 1) == 1) return;
            Dispatcher.InvokeAsync(() =>
            {
                Volatile.Write(ref _reloadQueued, 0);
                ScheduleReload();
            });
        };
        _rootWatcher.Created += onChange;
        _rootWatcher.Deleted += onChange;
        _rootWatcher.Renamed += (s, e) => onChange(s, e);
        _rootWatcher.EnableRaisingEvents = true;
    }

    private void ScheduleReload()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.InvokeAsync(ScheduleReload);
            return;
        }
        if (!IsVisible)
        {
            _dirty = true;
            return;
        }
        _reloadTimer.Stop();
        _reloadTimer.Start();
    }

    private async void Reload(bool keepScroll)
    {
        _dirty = false;
        int version = ++_reloadVersion;
        var (items, added) = await _s.ScanAsync();
        if (version != _reloadVersion) return;

        _library = items;
        _addedByPath = added;
        ForgetContentHits();
        BuildNav();
        BuildItems(keepScroll);
        if (_page == Page.Categories) CategoriesView.Refresh();
        if (_page == Page.History) HistoryView.Refresh();
    }

    private DateTime AddedOf(LibraryItem item) =>
        _addedByPath.TryGetValue(item.Path, out var time) ? time : item.Added;

    /// <summary>Kategoriler gösterim sırasıyla: her ana kategori, ardından alt kategorileri.</summary>
    private List<CategoryInfo> CategoryTree()
    {
        var cfg = _s.Config;
        lock (cfg)
        {
            return cfg.TreeOrder()
                .Select(c => new CategoryInfo(c, cfg.PathName(c), cfg.Parent(c) is not null, cfg.Children(c)))
                .ToList();
        }
    }

    /// <summary>Kenar çubuğunda bir kategori seçiliyse (ve arama yapılmıyorsa) o kategori.</summary>
    private Category? CurrentCategory()
    {
        if (SearchBox.Text.Trim().Length > 0 || !_navKey.StartsWith(CategoryPrefix, StringComparison.Ordinal)) return null;
        lock (_s.Config) return _s.Config.Find(_navKey[CategoryPrefix.Length..]);
    }

    private void BuildNav()
    {
        var counts = _library.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.Count());
        string CountOf(string id) => counts.TryGetValue(id, out int n) && n > 0 ? n.ToString() : "";

        // İşaret yalnızca kullanıcı bir sekmeyi seçtiyse gösterilir; seçmediyse "Tümü" sessizce varsayılandır.
        var defaultKey = _s.Config.DefaultView;
        var nav = new List<NavVM>
        {
            new() { Key = AllKey, IsDefault = defaultKey == AllKey, Title = "Tümü", Glyph = "", CountText = _library.Count > 0 ? _library.Count.ToString() : "" },
            new() { Key = RecentKey, IsDefault = defaultKey == RecentKey, Title = "Son eklenenler", Glyph = "" },
        };
        if (counts.ContainsKey(Library.InboxId))
            nav.Add(new() { Key = Library.InboxId, IsDefault = defaultKey == Library.InboxId, Title = "Gelen'de bekleyen", Glyph = InboxGlyph, CountText = CountOf(Library.InboxId) });

        nav.Add(new() { IsHeader = true, Title = "KATEGORİLER" });
        foreach (var info in CategoryTree())
        {
            nav.Add(new()
            {
                Key = CategoryPrefix + info.Category.Id,
                IsDefault = defaultKey == CategoryPrefix + info.Category.Id,
                Title = info.Category.Name,
                Glyph = Ui.Glyph(info.Category.Icon),
                Dot = Ui.BrushOf(info.Category.Color),
                CountText = CountOf(info.Category.Id),
                Category = info.Category,
                Indent = new Thickness(info.IsChild ? 18 : 0, 0, 0, 0),
            });
        }

        // Seçili kategori silinmiş olabilir; o zaman "Tümü"ne dön. Seçimi BuildItems yeniden kurar.
        _navKey = (nav.FirstOrDefault(n => n.Key == _navKey) ?? nav[0]).Key;
        _buildingNav = true;
        Nav.ItemsSource = nav;
        _buildingNav = false;
    }

    /// <summary>Arama her yerde yapıldığı için arama sürerken kenar çubuğunda hiçbir satır seçili görünmez.</summary>
    private void SyncNavSelection(bool searching)
    {
        var wanted = searching || _page != Page.Home
            ? null
            : (Nav.ItemsSource as List<NavVM>)?.FirstOrDefault(n => n.Key == _navKey);
        if (ReferenceEquals(Nav.SelectedItem, wanted)) return;

        _buildingNav = true;
        Nav.SelectedItem = wanted;
        _buildingNav = false;
    }

    private void BuildItems(bool keepScroll = false)
    {
        var tree = CategoryTree();
        var byId = tree.ToDictionary(t => t.Category.Id);
        var sortKey = _s.Config.SortBy;
        bool sortDescending = _s.Config.SortDescending;
        List<LibraryItem> Sorted(IEnumerable<LibraryItem> items) => LibrarySort.Apply(items, sortKey, sortDescending, AddedOf);

        ItemVM Vm(LibraryItem item, GroupVM group, string snippet = "")
        {
            byId.TryGetValue(item.CategoryId, out var info);
            return new ItemVM(item, group, item.CategoryId == Library.InboxId ? "Gelen" : info?.PathName ?? "", AddedOf(item))
            {
                CategoryGlyph = info is null ? InboxGlyph : Ui.Glyph(info.Category.Icon),
                CategoryDot = Ui.BrushOf(info?.Category.Color),
                Snippet = snippet,
            };
        }

        GroupVM GroupOf(CategoryInfo info, int count, bool more) => new()
        {
            Title = info.PathName,
            Glyph = Ui.Glyph(info.Category.Icon),
            Dot = Ui.BrushOf(info.Category.Color),
            CountText = count > MaxShown ? $"{count} (ilk {MaxShown} gösteriliyor)" : count.ToString(),
            Key = CategoryPrefix + info.Category.Id,
            MoreVisibility = more ? Visibility.Visible : Visibility.Collapsed,
        };

        var rawQuery = SearchBox.Text.Trim();
        bool searching = rawQuery.Length > 0;
        var list = new List<ItemVM>();
        string note = "";
        SyncNavSelection(searching);
        if (!searching) ForgetContentHits();

        if (searching)
        {
            var found = LibrarySearch.Find(_library, rawQuery, AddedOf, MaxShown);
            if (found.Extension is null)
            {
                var group = new GroupVM { Title = $"“{rawQuery}” için sonuçlar", CountText = found.Count.ToString() };
                list.AddRange(found.ByName.Select(i => Vm(i, group)));
            }
            else
            {
                // Yalnızca uzantı yazıldı: o türdeki dosyalar önce, adında geçenler ayrı grupta.
                if (found.ByExtension.Count > 0)
                {
                    var byType = new GroupVM { Title = $"“{found.Extension}” türündeki dosyalar", CountText = found.ByExtension.Count.ToString() };
                    list.AddRange(found.ByExtension.Select(i => Vm(i, byType)));
                }
                if (found.ByName.Count > 0)
                {
                    var byName = new GroupVM { Title = $"Adında “{rawQuery}” geçenler", CountText = found.ByName.Count.ToString() };
                    list.AddRange(found.ByName.Select(i => Vm(i, byName)));
                }
            }

            list.AddRange(ContentResults(rawQuery, skipContent: found.Extension is not null, shown: list, Vm));
            note = (found.Extension is null
                ? $"Raf'ta “{rawQuery}” ile eşleşen bir şey yok."
                : $"Raf'ta “{found.Extension}” türünde dosya yok.")
                + $"\nUygulamalar ve masaüstündekiler hızlı aramada ({_s.Config.Hotkey}) çıkar.";
        }
        else if (_navKey == RecentKey)
        {
            var group = new GroupVM { Title = "Son eklenenler" };
            list.AddRange(_library.OrderByDescending(AddedOf).Take(RecentCount).Select(i => Vm(i, group)));
        }
        else if (_navKey == AllKey)
        {
            var lookup = _library.ToLookup(i => i.CategoryId);
            var waiting = Sorted(lookup[Library.InboxId]);
            if (waiting.Count > 0)
            {
                var group = new GroupVM
                {
                    Title = "Gelen'de bekleyen",
                    CountText = waiting.Count.ToString(),
                    Key = Library.InboxId,
                    MoreVisibility = waiting.Count > PreviewPerCategory ? Visibility.Visible : Visibility.Collapsed,
                };
                list.AddRange(waiting.Take(PreviewPerCategory).Select(i => Vm(i, group)));
            }

            foreach (var info in tree)
            {
                var all = Sorted(lookup[info.Category.Id]);
                if (all.Count == 0) continue;
                var group = GroupOf(info, all.Count, more: all.Count > PreviewPerCategory);
                list.AddRange(all.Take(PreviewPerCategory).Select(i => Vm(i, group)));
            }
        }
        else if (_navKey == Library.InboxId)
        {
            var all = Sorted(_library.Where(i => i.CategoryId == Library.InboxId));
            var group = new GroupVM { Title = "Gelen'de bekleyen", CountText = all.Count.ToString() };
            list.AddRange(all.Take(MaxShown).Select(i => Vm(i, group)));
            note = "Gelen klasöründe bekleyen bir şey yok.";
        }
        else if (byId.TryGetValue(_navKey[CategoryPrefix.Length..], out var selected))
        {
            // Bir kategori: önce kendi öğeleri, ardından alt kategorilerinin her biri kendi başlığıyla.
            var sections = new List<CategoryInfo> { selected };
            sections.AddRange(selected.Children.Select(child => byId[child.Id]));
            foreach (var info in sections)
            {
                var all = Sorted(_library.Where(i => i.CategoryId == info.Category.Id));
                if (all.Count == 0 && info != selected) continue;
                if (all.Count == 0 && sections.Count > 1) continue;
                var group = GroupOf(info, all.Count, more: false);
                list.AddRange(all.Take(MaxShown).Select(i => Vm(i, group)));
            }
            note = "Bu kategoride henüz bir şey yok.\nDosyaları buraya sürükleyebilir, yapıştırabilir (Ctrl+V) ya da “Kategoriler ve kurallar”dan kural ekleyebilirsin.";
        }

        double offset = keepScroll ? Ui.Descendant<ScrollViewer>(ItemsList)?.VerticalOffset ?? 0 : 0;
        var selectedPaths = keepScroll
            ? new HashSet<string>(Selected().Select(v => v.Item.Path), StringComparer.OrdinalIgnoreCase)
            : null;

        var view = new ListCollectionView(list);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ItemVM.Group)));
        ItemsList.ItemsSource = view;

        if (selectedPaths is { Count: > 0 })
        {
            foreach (var vm in list.Where(v => selectedPaths.Contains(v.Item.Path)))
                ItemsList.SelectedItems.Add(vm);
        }
        Dispatcher.InvokeAsync(() => Ui.Descendant<ScrollViewer>(ItemsList)?.ScrollToVerticalOffset(offset), DispatcherPriority.Loaded);

        bool welcome = _library.Count == 0 && !searching;
        if (welcome) UpdateWelcome();
        Welcome.Visibility = welcome ? Visibility.Visible : Visibility.Collapsed;
        // Liste boşken de görünür kalır ki boş alana sağ tıklanıp yapıştırılabilsin, klasör açılabilsin.
        ItemsList.Visibility = welcome ? Visibility.Collapsed : Visibility.Visible;
        EmptyNote.Text = note;
        EmptyNote.Visibility = !welcome && list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DetailsHeader.Visibility = _s.Config.DetailsView && !welcome && list.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    // ===== Gezinme =====

    private void ShowPage(Page page)
    {
        _page = page;
        HomePanel.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        ViewTools.Visibility = page == Page.Home ? Visibility.Visible : Visibility.Collapsed;
        CategoriesView.Visibility = page == Page.Categories ? Visibility.Visible : Visibility.Collapsed;
        HistoryView.Visibility = page == Page.History ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = page == Page.Settings ? Visibility.Visible : Visibility.Collapsed;

        if (page == Page.Categories) CategoriesView.Refresh();
        if (page == Page.History) HistoryView.Refresh();
        if (page == Page.Settings) SettingsView.Refresh();
    }

    private void GoHome(string? navKey = null)
    {
        if (navKey is not null) _navKey = navKey;
        CategoriesRadio.IsChecked = HistoryRadio.IsChecked = SettingsRadio.IsChecked = false;
        ShowPage(Page.Home);
        BuildItems();
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_buildingNav || Nav.SelectedItem is not NavVM selected) return;

        // Arama sırasında bir kategoriye tıklamak "oraya git" demektir; arama kapanır.
        _navKey = selected.Key;
        if (SearchBox.Text.Length > 0) SearchBox.Clear();
        GoHome(selected.Key);
    }

    private void PageRadio_Checked(object sender, RoutedEventArgs e)
    {
        _buildingNav = true;
        Nav.SelectedItem = null;
        _buildingNav = false;

        ShowPage(sender == CategoriesRadio ? Page.Categories
               : sender == HistoryRadio ? Page.History
               : Page.Settings);
    }

    private void GoCategories_Click(object sender, RoutedEventArgs e) => CategoriesRadio.IsChecked = true;

    private void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GroupVM { Key.Length: > 0 } group) GoHome(group.Key);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsInitialized) return;
        if (_page != Page.Home) GoHome();
        else BuildItems();
    }

    // ===== Klavye =====

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool typing = Keyboard.FocusedElement is TextBox;
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;

        if (e.Key == Key.Escape)
        {
            if (_page != Page.Home) GoHome();
            else if (SearchBox.Text.Length > 0) SearchBox.Clear();
            else HideLauncher();
            e.Handled = true;
        }
        else if (e.Key == Key.F && ctrl)
        {
            if (_page != Page.Home) GoHome();
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.V && ctrl && !typing && _page == Page.Home)
        {
            Paste();
            e.Handled = true;
        }
        else if (e.Key == Key.F5)
        {
            Reload(keepScroll: true);
            e.Handled = true;
        }
    }

    /// <summary>Liste odaktayken yazmaya başlamak aramayı başlatır.</summary>
    private void Window_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (_page != Page.Home || string.IsNullOrEmpty(e.Text) || char.IsControl(e.Text[0])) return;
        if (Keyboard.FocusedElement is TextBox) return;

        SearchBox.Focus();
        SearchBox.Text += e.Text;
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ItemsList.Items.Count == 0) return;

        if (e.Key == Key.Enter)
        {
            OpenItems(new[] { (ItemVM)ItemsList.Items[0]! });
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            var first = ItemsList.Items[0];
            ItemsList.SelectedItem = first;
            ItemsList.UpdateLayout();
            (ItemsList.ItemContainerGenerator.ContainerFromItem(first) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void ItemsList_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        bool none = Keyboard.Modifiers == ModifierKeys.None;

        if (e.Key == Key.Enter && none) OpenItems(Selected());
        else if (e.Key == Key.System && e.SystemKey == Key.Enter) ShowPropertiesOfSelected();
        else if (e.Key == Key.F2 && none) RenameSelected();
        else if (e.Key == Key.Delete && none) DeleteSelected();
        else if (e.Key == Key.C && ctrl) CopySelected(cut: false);
        else if (e.Key == Key.X && ctrl) CopySelected(cut: true);
        else return;

        e.Handled = true;
    }

    // ===== Karşılama =====

    private async void UpdateWelcome()
    {
        HotkeyNote.Text = $"{_s.Config.Hotkey} her yerden hızlı aramayı açar: yaz, Enter'a bas. Dosya ve klasörleri bu pencereye sürükleyip bırakarak da ekleyebilirsin.";

        bool watching = _s.IsWatching(Shell.Downloads());
        DownloadsButton.IsEnabled = !watching;
        DownloadsButton.Content = watching ? "İzleniyor" : "İzlemeye al";

        // Sabitleme Raf'ın dışında (Dosya Gezgini'nden) de yapılıp kaldırılabilir; bir kez sabitli görülünce yeniden sorulmaz.
        if (!_inboxPinned && await Shell.IsPinnedAsync(_s.Inbox)) _inboxPinned = true;
        if (_inboxPinned) ShowPinned();
    }

    private void ShowPinned()
    {
        PinButton.IsEnabled = false;
        PinButton.Content = "Sabitlendi";
        PinNote.Text = "Tamam. Kaydederken sol taraftaki “Gelen”e tıklaman yeterli.";
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        PinButton.IsEnabled = false;
        _inboxPinned = await Shell.PinToQuickAccessAsync(_s.Inbox);
        if (_inboxPinned)
        {
            ShowPinned();
            return;
        }

        PinButton.IsEnabled = true;
        PinNote.Text = "Otomatik sabitlenemedi. Raf klasörünü açıp “Gelen”e sağ tıklayarak “Hızlı Erişim'e sabitle” diyebilirsin.";
        Shell.OpenFolder(_s.Config.Root);
    }

    private void WatchDownloads_Click(object sender, RoutedEventArgs e)
    {
        var error = _s.Watch(Shell.Downloads());
        if (error is not null) Views.MessageDialog.Info(this, "İndirilenler izlenemiyor", error);
        UpdateWelcome();
    }
}
