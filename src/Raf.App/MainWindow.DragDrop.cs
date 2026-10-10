using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Raf.App.Views;
using Raf.Core;

namespace Raf.App;

// Sürükle-bırak: öğeleri kategoriler arasında taşıma, dışarı sürükleme ve dışarıdan Raf'a ekleme.
public partial class MainWindow
{
    private const string InternalDrag = "Raf.Internal";

    private Point _dragStart;
    private bool _dragArmed;
    private ItemVM? _collapseOnRelease;
    private ListBoxItem? _dropTarget;

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _collapseOnRelease = null;
        var container = Ui.Ancestor<ListBoxItem>(e.OriginalSource);
        _dragArmed = container is not null;

        // Çoklu seçimin içinden bir öğeye basmak normalde seçimi teke indirir ve hepsini birden
        // sürüklemeyi imkânsız kılar. Seçimi koru; sürükleme olmazsa bırakınca teke indir.
        if (container is { IsSelected: true, DataContext: ItemVM vm }
            && ItemsList.SelectedItems.Count > 1
            && Keyboard.Modifiers == ModifierKeys.None
            && e.ClickCount == 1)
        {
            _collapseOnRelease = vm;
            container.Focus();
            e.Handled = true;
        }
    }

    private void ItemsList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragArmed = false;
        if (_collapseOnRelease is { } vm)
        {
            _collapseOnRelease = null;
            ItemsList.SelectedItems.Clear();
            ItemsList.SelectedItem = vm;
        }
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _dragArmed = false;
        _collapseOnRelease = null;
        var selected = Selected();
        if (selected.Count == 0) return;

        var files = new StringCollection();
        files.AddRange(selected.Select(v => v.Item.Path).ToArray());
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData(InternalDrag, "1");
        DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Move | DragDropEffects.Copy | DragDropEffects.Link);
        SetDropTarget(null);
    }

    private void SetDropTarget(ListBoxItem? item)
    {
        if (ReferenceEquals(_dropTarget, item)) return;
        if (_dropTarget is not null) _dropTarget.Tag = null;
        _dropTarget = item;
        if (item is not null) item.Tag = "drop";
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;
        e.Effects = DragDropEffects.None;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var navItem = Ui.Ancestor<ListBoxItem>(e.OriginalSource) is { DataContext: NavVM { Category: not null } } hit ? hit : null;
        SetDropTarget(navItem);

        if (e.Data.GetDataPresent(InternalDrag))
        {
            if (navItem is not null) e.Effects = DragDropEffects.Move;
        }
        else if (_page == Page.Home)
        {
            // Dosya Gezgini'ne "taşı" denirse kaynağı kendisi siler; taşımayı Raf yapacağı için "kopyala" bildirilir.
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Copy) ? DragDropEffects.Copy : DragDropEffects.Link;
        }
    }

    private void Window_DragLeave(object sender, DragEventArgs e) => SetDropTarget(null);

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var target = (_dropTarget?.DataContext as NavVM)?.Category;
        SetDropTarget(null);
        e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;

        if (e.Data.GetDataPresent(InternalDrag))
        {
            if (target is null) return;
            var items = paths
                .Select(p => _library.FirstOrDefault(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase)))
                .OfType<LibraryItem>()
                .ToList();
            MoveItems(items, target);
            return;
        }

        if (_page != Page.Home) return;
        // Soru penceresi bırakma işlemi bitmeden açılırsa Dosya Gezgini o süre boyunca donar.
        Dispatcher.InvokeAsync(() => AddExternal(paths, target));
    }

    /// <summary>Dışarıdan sürüklenen öğeleri taşıyarak ya da bağlantı olarak Raf'a ekler.</summary>
    private async void AddExternal(string[] paths, Category? target)
    {
        var cfg = _s.Config;
        target ??= CurrentCategory();
        var inside = paths.Where(p => RafLayout.IsInside(p, cfg.Root)).ToList();
        var outside = paths.Except(inside).ToList();

        bool asLink = false;
        if (outside.Count > 0)
        {
            Activate();
            var title = outside.Count == 1
                ? $"“{Path.GetFileName(RafLayout.Normalize(outside[0]))}” Raf'a nasıl eklensin?"
                : $"{outside.Count} öğe Raf'a nasıl eklensin?";
            string where;
            lock (cfg) where = target is null ? "kurallara göre uygun kategoriye" : $"“{cfg.PathName(target)}” kategorisine";
            int choice = MessageDialog.Ask(this, title,
                $"Taşı: öğe şu anki yerinden alınır ve {where} konur.\n\n" +
                "Bağlantı ekle: öğe olduğu yerde kalır, Raf'ta yalnızca kısayolu görünür. " +
                "Proje klasörleri gibi yeri değişmemesi gerekenler için bunu seç.",
                "Taşı", "Bağlantı ekle", "Vazgeç");
            if (choice == 1) asLink = true;
            else if (choice != 0) outside.Clear();
        }
        if (inside.Count + outside.Count == 0) return;

        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var path in inside.Concat(outside))
            {
                var name = Path.GetFileName(RafLayout.Normalize(path));
                try
                {
                    var category = target ?? _s.Sorter.Classify(path);
                    if (asLink && !inside.Contains(path))
                    {
                        CategoryService.AddLink(cfg, _s.Store, path, category);
                        continue;
                    }

                    string? why;
                    lock (cfg) why = RafLayout.WhyNotMovable(cfg, path);
                    if (why is not null) failed.Add($"{name}: {why}");
                    else _s.Sorter.MoveTo(path, category);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{name}: {e.Message}");
                }
            }
            return failed;
        });

        ScheduleReload();
        if (errors.Count > 0) MessageDialog.Info(this, "Bazı öğeler eklenemedi", string.Join("\n", errors));
    }
}
