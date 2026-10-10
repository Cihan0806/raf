using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Raf.App.Views;
using Raf.Core;

namespace Raf.App;

// Öğeler üzerindeki işlemler: açma, sağ tık menüleri, dosya işlemleri, pano, taşıma bildirimi ve geri alma.
public partial class MainWindow
{
    private const int MaxOpenAtOnce = 10;

    private readonly List<MoveRecord> _notifyBatch = new();
    private MoveRecord? _snackRecord;
    private Action? _snackAction;

    private List<ItemVM> Selected() => ItemsList.SelectedItems.Cast<ItemVM>().ToList();

    // ===== Açma =====

    private void ItemsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (Ui.Ancestor<ListBoxItem>(e.OriginalSource)?.DataContext is ItemVM vm) OpenItems(new[] { vm });
    }

    private void OpenItems(IReadOnlyList<ItemVM> items)
    {
        if (items.Count == 0) return;

        var errors = new List<string>();
        foreach (var vm in items.Take(MaxOpenAtOnce))
        {
            try { Shell.Open(vm.Item.Path); }
            catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
            {
                errors.Add($"{vm.Item.Name}: {e.Message}");
            }
        }

        if (errors.Count > 0) MessageDialog.Info(this, "Açılamadı", string.Join("\n", errors));
        else HideLauncher();
    }

    private void ShowPropertiesOfSelected()
    {
        var selected = Selected();
        if (selected.Count == 1) Shell.ShowProperties(selected[0].Item.Path);
    }

    // ===== Sağ tık menüleri =====

    private void ItemsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = Ui.Ancestor<ListBoxItem>(e.OriginalSource);
        if (item is null)
        {
            // Boş alana sağ tık: seçim kalkar, menü listenin kendisi için açılır.
            ItemsList.SelectedItems.Clear();
        }
        else if (!item.IsSelected)
        {
            // Seçili olmayan bir öğeye sağ tıklanırsa menü o öğe için açılsın.
            ItemsList.SelectedItems.Clear();
            item.IsSelected = true;
        }
    }

    private void ItemsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = ItemsList.ContextMenu!;
        menu.Items.Clear();

        var selected = Selected();
        bool onItem = selected.Count > 0 && Ui.Ancestor<ListBoxItem>(e.OriginalSource) is not null;
        if (onItem) FillItemMenu(menu, selected);
        else FillBackgroundMenu(menu);
    }

    private void FillItemMenu(ContextMenu menu, List<ItemVM> selected)
    {
        var items = selected.Select(v => v.Item).ToList();
        bool single = items.Count == 1;

        menu.Items.Add(MenuEntry("Aç", "", () => OpenItems(selected), "Enter"));
        if (single) menu.Items.Add(MenuEntry("Dosya konumunu göster", "", () => Shell.Reveal(items[0].Path)));
        menu.Items.Add(new Separator());

        menu.Items.Add(MenuEntry("Kes", "", () => CopySelected(cut: true), "Ctrl+X"));
        menu.Items.Add(MenuEntry("Kopyala", "", () => CopySelected(cut: false), "Ctrl+C"));
        menu.Items.Add(new Separator());

        var move = MenuEntry("Şuraya taşı", "", null);
        foreach (var info in CategoryTree())
        {
            var entry = new MenuItem
            {
                Header = info.Category.Name,
                Icon = new TextBlock
                {
                    Text = Ui.Glyph(info.Category.Icon),
                    Width = 16,
                    FontSize = 14,
                    // Alt kategoriler, üst kategorinin altında içeriden başlar.
                    Margin = new Thickness(info.IsChild ? 16 : 0, 0, 0, 0),
                    Style = (Style)FindResource("Icon"),
                    Foreground = Ui.BrushOf(info.Category.Color),
                },
            };
            entry.Click += (_, _) => MoveItems(items, info.Category);
            move.Items.Add(entry);
        }
        menu.Items.Add(move);

        if (items.Any(i => i.CategoryId == Library.InboxId))
            menu.Items.Add(MenuEntry("Kurallara göre yerleştir", "", () => SortByRules(items.Where(i => i.CategoryId == Library.InboxId).ToList())));
        menu.Items.Add(new Separator());

        if (single) menu.Items.Add(MenuEntry("Yeniden adlandır", "", RenameSelected, "F2"));
        bool onlyLinks = items.All(i => i.IsLink);
        menu.Items.Add(MenuEntry(onlyLinks ? "Bağlantıyı Raf'tan kaldır" : "Sil", onlyLinks ? "" : "", DeleteSelected, "Del"));

        if (single)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("Özellikler", "", ShowPropertiesOfSelected, "Alt+Enter"));
        }
    }

    private void FillBackgroundMenu(ContextMenu menu)
    {
        bool canPaste = false;
        try { canPaste = Clipboard.ContainsFileDropList(); }
        catch (ExternalException)
        {
            // Pano o an başka bir programca kilitli; yapıştırma kapalı görünür.
        }

        var paste = MenuEntry("Yapıştır", "", Paste, "Ctrl+V");
        paste.IsEnabled = canPaste;
        menu.Items.Add(paste);

        var category = CurrentCategory();
        if (category is not null) menu.Items.Add(MenuEntry("Yeni klasör", "", () => NewFolder(category)));
        menu.Items.Add(new Separator());

        var view = MenuEntry("Görünüm", "", null);
        view.Items.Add(Choice("Kutucuklar", !_s.Config.DetailsView, () => SetDetailsView(false)));
        view.Items.Add(Choice("Ayrıntı listesi", _s.Config.DetailsView, () => SetDetailsView(true)));
        view.Items.Add(new Separator());
        view.Items.Add(Choice("Önizleme bölmesi", _s.Config.ShowPreview, () => SetPreview(!_s.Config.ShowPreview)));
        menu.Items.Add(view);

        var sort = MenuEntry("Sırala", "", null);
        foreach (var entry in SortEntries()) sort.Items.Add(entry);
        menu.Items.Add(sort);

        menu.Items.Add(new Separator());
        menu.Items.Add(MenuEntry("Yenile", "", () => Reload(keepScroll: true), "F5"));
    }

    /// <param name="glyph">Simge karakteri; boşsa simgenin yeri boş bırakılır (satırlar hizalı kalsın diye).</param>
    private MenuItem MenuEntry(string header, string glyph, Action? action, string gesture = "")
    {
        var icon = new TextBlock { Text = glyph, Width = 16, FontSize = 14, Style = (Style)FindResource("Icon") };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var entry = new MenuItem { Header = header, Icon = icon, InputGestureText = gesture };
        if (action is not null) entry.Click += (_, _) => action();
        return entry;
    }

    // ===== Dosya işlemleri =====

    private void RenameSelected()
    {
        var selected = Selected();
        if (selected.Count != 1) return;

        var item = selected[0].Item;
        Category? category;
        lock (_s.Config) category = _s.Config.Find(item.CategoryId);

        InputDialog.Ask(this, $"“{item.Name}” için yeni ad", item.Name, "Yeniden adlandır", selectStem: !item.IsDirectory, apply: name =>
        {
            try
            {
                _s.Sorter.Rename(item.Path, name, category);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        });
    }

    /// <summary>
    /// Seçili öğeleri Geri Dönüşüm Kutusu'na gönderir. Bağlantılar silinmez: yalnızca Raf'tan
    /// kaldırılır, asıl öğe yerinde kalır.
    /// </summary>
    private void DeleteSelected()
    {
        var items = Selected().Select(v => v.Item).ToList();
        if (items.Count == 0) return;

        var links = items.Where(i => i.IsLink).ToList();
        var real = items.Where(i => !i.IsLink).ToList();

        if (real.Count > 0)
        {
            var title = real.Count == 1
                ? $"“{real[0].Name}” Geri Dönüşüm Kutusu'na gönderilsin mi?"
                : $"{real.Count} öğe Geri Dönüşüm Kutusu'na gönderilsin mi?";
            var body = "Vazgeçersen oradan geri alabilirsin."
                + (links.Count > 0 ? $"\n\nSeçimdeki {links.Count} bağlantı silinmez; yalnızca Raf'tan kaldırılır, asıl öğeler yerinde kalır." : "");
            if (MessageDialog.Ask(this, title, body, "Gönder", "Vazgeç") != 0) return;
        }

        if (links.Count > 0) RemoveLinks(links);

        // Windows'un kendi silme pencereleri (ör. "kutuya sığmıyor") arayüz iş parçacığında açılmalı.
        var errors = new List<string>();
        int sent = 0;
        foreach (var item in real)
        {
            try
            {
                Shell.Recycle(item.Path);
                sent++;
            }
            catch (OperationCanceledException)
            {
                // Kullanıcı Windows'un sorusunda vazgeçti.
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{item.Name}: {e.Message}");
            }
        }

        if (sent > 0)
        {
            ShowSnack(sent == 1 && real.Count == 1
                ? $"“{real[0].Name}” Geri Dönüşüm Kutusu'na gönderildi."
                : $"{sent} öğe Geri Dönüşüm Kutusu'na gönderildi.", null);
        }
        ScheduleReload();
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler silinemedi", string.Join("\n", errors));
    }

    private void CopySelected(bool cut)
    {
        var selected = Selected();
        if (selected.Count == 0) return;

        try
        {
            Shell.CopyFiles(selected.Select(v => v.Item.Path), cut);
        }
        catch (ExternalException)
        {
            ShowSnack("Pano şu an başka bir program tarafından kullanılıyor; yeniden dene.", null);
            return;
        }

        var what = selected.Count == 1 ? $"“{selected[0].Item.Name}”" : $"{selected.Count} öğe";
        ShowSnack(cut ? $"{what} kesildi; yapıştırdığın yere taşınacak." : $"{what} panoya kopyalandı.", null);
    }

    /// <summary>
    /// Panodaki dosyaları açık olan kategoriye (kategori seçili değilse kurallara göre) yapıştırır.
    /// Kopyalanmışsa kopyası alınır, kesilmişse taşınır.
    /// </summary>
    private async void Paste()
    {
        string[] paths;
        bool cut;
        try
        {
            (paths, cut) = Shell.FilesOnClipboard();
        }
        catch (ExternalException)
        {
            ShowSnack("Pano şu an başka bir program tarafından kullanılıyor; yeniden dene.", null);
            return;
        }
        if (paths.Length == 0)
        {
            ShowSnack("Panoda yapıştırılacak dosya yok.", null);
            return;
        }

        var target = CurrentCategory();
        var cfg = _s.Config;
        int done = 0;
        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var path in paths)
            {
                var name = Path.GetFileName(RafLayout.Normalize(path));
                try
                {
                    var category = target ?? _s.Sorter.Classify(path);

                    bool isLink;
                    string? blocked;
                    lock (cfg)
                    {
                        isLink = cfg.Links.Any(l => RafLayout.IsSame(l.Path, path));
                        blocked = RafLayout.IsStructural(cfg, path) ? "Bu, Raf'ın kendi klasörlerinden biri."
                                : cut && !RafLayout.IsInside(path, cfg.Root) ? RafLayout.WhyNotMovable(cfg, path)
                                : null;
                    }

                    if (isLink)
                    {
                        // Bağlantının asıl öğesi yerinde kalmalı; yalnızca bağlandığı kategori değişir.
                        CategoryService.AddLink(cfg, _s.Store, path, category);
                    }
                    else if (blocked is not null)
                    {
                        failed.Add($"{name}: {blocked}");
                        continue;
                    }
                    else if (cut) _s.Sorter.MoveTo(path, category);
                    else _s.Sorter.CopyTo(path, category);
                    done++;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{name}: {e.Message}");
                }
            }
            return failed;
        });

        if (cut)
        {
            // Kesilenler taşındı; pano aynı dosyaları ikinci kez taşımaya çalışmasın.
            try { Clipboard.Clear(); }
            catch (ExternalException) { }
        }
        if (done > 0 && !cut) ShowSnack(done == 1 ? "1 öğe yapıştırıldı." : $"{done} öğe yapıştırıldı.", null);
        ScheduleReload();
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler yapıştırılamadı", string.Join("\n", errors));
    }

    private void NewFolder(Category category)
    {
        string dir;
        lock (_s.Config) dir = RafLayout.Dir(_s.Config, category);

        bool created = InputDialog.Ask(this, $"“{category.Name}” içinde yeni klasör", "Yeni klasör", "Oluştur", selectStem: false, apply: name =>
        {
            if (RafLayout.WhyNotValidName(name) is { } why) return why;
            var path = Path.Combine(dir, name);
            if (RafLayout.Exists(path)) return $"“{name}” adında bir öğe zaten var.";
            try
            {
                Directory.CreateDirectory(path);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return e.Message;
            }
        });
        if (created) ScheduleReload();
    }

    private async void MoveItems(IReadOnlyList<LibraryItem> items, Category target)
    {
        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var item in items)
            {
                try
                {
                    if (item.IsLink) CategoryService.AddLink(_s.Config, _s.Store, item.Path, target);
                    else _s.Sorter.MoveTo(item.Path, target);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{item.Name}: {e.Message}");
                }
            }
            return failed;
        });

        ScheduleReload();
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler taşınamadı", string.Join("\n", errors));
    }

    private async void SortByRules(IReadOnlyList<LibraryItem> items)
    {
        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var item in items)
            {
                try { _s.Sorter.Sort(item.Path, auto: false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{item.Name}: {e.Message}");
                }
            }
            return failed;
        });

        ScheduleReload();
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler taşınamadı", string.Join("\n", errors));
    }

    private void RemoveLinks(IReadOnlyList<LibraryItem> links)
    {
        foreach (var link in links) CategoryService.RemoveLink(_s.Config, _s.Store, link.Path);
        ShowSnack(links.Count == 1
            ? $"“{links[0].Name}” bağlantısı kaldırıldı; asıl öğe yerinde duruyor."
            : $"{links.Count} bağlantı kaldırıldı; asıl öğeler yerinde duruyor.", null);
        ScheduleReload();
    }

    // ===== Taşıma bildirimi ve geri alma =====

    private void OnMoved(MoveRecord record)
    {
        ScheduleReload();
        if (_quietMoves) return;
        var oldName = Path.GetFileName(record.From);
        var newName = Path.GetFileName(record.To);

        if (record.Undone)
        {
            ShowSnack(record.Renamed ? $"“{newName}” yeniden “{oldName}” oldu." : $"“{oldName}” eski yerine döndü.", null);
            return;
        }

        ShowSnack(record.Renamed ? $"“{oldName}”  →  “{newName}”" : $"“{newName}”  →  {record.CategoryName}", record);
        if (record.Auto && _s.Config.Notify && !IsActive)
        {
            _notifyBatch.Add(record);
            _notifyTimer.Stop();
            _notifyTimer.Start();
        }
    }

    private void FlushNotifications()
    {
        _notifyTimer.Stop();
        if (_notifyBatch.Count == 0) return;

        if (_notifyBatch.Count == 1)
        {
            var record = _notifyBatch[0];
            _s.Tray?.Notify("Raf düzenledi", $"{Path.GetFileName(record.To)}  →  {record.CategoryName}");
        }
        else
        {
            var summary = _notifyBatch.GroupBy(r => r.CategoryName).Select(g => $"{g.Key}: {g.Count()}");
            _s.Tray?.Notify($"Raf {_notifyBatch.Count} öğeyi düzenledi", string.Join(",  ", summary));
        }
        _notifyBatch.Clear();
    }

    private void ShowSnack(string text, MoveRecord? undoable)
    {
        _snackRecord = undoable;
        _snackAction = null;
        SnackText.Text = text;
        SnackUndo.Visibility = undoable is null ? Visibility.Collapsed : Visibility.Visible;
        Snack.Visibility = Visibility.Visible;
        _snackTimer.Stop();
        _snackTimer.Start();
    }

    /// <summary>Tek bir taşıma kaydına bağlı olmayan, geri alınabilir bir iş için. Kapatılana ya da yenisi gelene kadar durur.</summary>
    private void ShowSnackAction(string text, Action undo)
    {
        ShowSnack(text, null);
        _snackAction = undo;
        SnackUndo.Visibility = Visibility.Visible;
        _snackTimer.Stop();
    }

    private void HideSnack()
    {
        _snackTimer.Stop();
        _snackRecord = null;
        _snackAction = null;
        Snack.Visibility = Visibility.Collapsed;
    }

    private void SnackClose_Click(object sender, RoutedEventArgs e) => HideSnack();

    private void SnackUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_snackAction is { } action)
        {
            HideSnack();
            action();
            return;
        }
        if (_snackRecord is not { } record) return;
        HideSnack();
        var error = _s.Sorter.Undo(record);
        if (error is not null) MessageDialog.Info(this, "Geri alınamadı", error);
    }
}
