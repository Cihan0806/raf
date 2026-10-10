using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Raf.Core;

namespace Raf.App.Views;

public partial class CategoriesPage : UserControl
{
    private const int PreviewLines = 6;

    public CategoriesPage()
    {
        InitializeComponent();
    }

    private static Services S => App.S;

    private Window? Owner => Window.GetWindow(this);

    public sealed class Row
    {
        public required Category Category { get; init; }
        public required string Order { get; init; }
        public required string Name { get; init; }
        public required string Glyph { get; init; }
        public required Brush Dot { get; init; }
        public required string Summary { get; init; }
        public required string CountText { get; init; }
        public bool CanUp { get; init; }
        public bool CanDown { get; init; }
        public Visibility MoveVisibility { get; init; }

        /// <summary>Alt kategori satırları içeriden başlar.</summary>
        public Thickness RowMargin { get; init; }

        /// <summary>"Alt kategori ekle" düğmesi yalnızca ana kategorilerde görünür.</summary>
        public Visibility AddChildVisibility { get; init; }
    }

    public void Refresh()
    {
        var cfg = S.Config;
        var counts = Library.Scan(cfg).GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.Count());

        var rows = new List<Row>();
        lock (cfg)
        {
            int rank = 0;
            foreach (var category in cfg.TreeOrder())
            {
                var parent = cfg.Parent(category);
                // Yukarı/aşağı, aynı seviyedeki komşular arasındadır; "Diğer" hep en sondadır.
                var siblings = cfg.Categories.Where(c => cfg.Parent(c) == parent && !c.IsFallback).ToList();
                int index = siblings.IndexOf(category);
                int count = counts.GetValueOrDefault(category.Id);

                rows.Add(new Row
                {
                    Category = category,
                    Order = parent is null && !category.IsFallback ? (++rank).ToString() : "",
                    Name = category.Name,
                    Glyph = Ui.Glyph(category.Icon),
                    Dot = Ui.BrushOf(category.Color),
                    Summary = Summarize(category),
                    CountText = count == 0 ? "boş" : $"{count} öğe",
                    CanUp = index > 0,
                    CanDown = index >= 0 && index < siblings.Count - 1,
                    MoveVisibility = category.IsFallback ? Visibility.Collapsed : Visibility.Visible,
                    RowMargin = new Thickness(parent is null ? 0 : 30, 0, 0, 6),
                    AddChildVisibility = parent is null && !category.IsFallback ? Visibility.Visible : Visibility.Collapsed,
                });
            }
        }
        Rows.ItemsSource = rows;
        UpdateTest();
    }

    private static string Summarize(Category category)
    {
        if (category.IsFallback) return "Başka hiçbir kategoriye uymayan her şey buraya düşer.";
        if (!category.HasRules) return "Kural yok: yalnızca elle taşıdıkların girer.";

        var parts = new List<string>();
        if (category.Extensions.Count > 0) parts.Add("Tür: " + string.Join(" ", category.Extensions));
        if (category.NameContains.Count > 0) parts.Add("Adında: " + string.Join(", ", category.NameContains));
        if (category.Sites.Count > 0) parts.Add("Site: " + string.Join(", ", category.Sites));
        return string.Join(category.MatchAll ? "   ve   " : "   ·   ", parts);
    }

    private static Category CategoryOf(object sender) => ((Row)((FrameworkElement)sender).Tag).Category;

    private void Add_Click(object sender, RoutedEventArgs e) => AddCategory(parentId: "");

    private void AddChild_Click(object sender, RoutedEventArgs e) => AddCategory(CategoryOf(sender).Id);

    private void AddCategory(string parentId)
    {
        var dialog = new CategoryDialog(S.Config, null, parentId) { Owner = Owner };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        try
        {
            CategoryService.Add(S.Config, S.Store, dialog.Result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Info(Owner, "Kategori eklenemedi", ex.Message);
            return;
        }
        AfterChange(offerReapply: dialog.Result.HasRules);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        var category = CategoryOf(sender);
        var dialog = new CategoryDialog(S.Config, category) { Owner = Owner };
        if (dialog.ShowDialog() != true || dialog.Result is null) return;

        try
        {
            CategoryService.Update(S.Config, S.Store, category, dialog.Result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Info(Owner, "Kategori güncellenemedi",
                "Klasör yeniden adlandırılamadı; içindeki bir dosya açık olabilir.\n\n" + ex.Message);
            return;
        }
        AfterChange(offerReapply: true);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var category = CategoryOf(sender);
        int count = Library.Scan(S.Config).Count(i => i.CategoryId == category.Id);
        string fallback;
        int children;
        lock (S.Config)
        {
            fallback = S.Config.Fallback.Name;
            children = S.Config.Children(category).Count;
        }

        var body = count == 0
            ? "Kategori boş; yalnızca kendisi kaldırılır."
            : $"İçindeki {count} öğe silinmez, “{fallback}” kategorisine taşınır.";
        if (children > 0) body += $"\n\n{children} alt kategorisi silinmez; içindekilerle birlikte ana kategori olur.";
        if (MessageDialog.Ask(Owner, $"“{category.Name}” kategorisi silinsin mi?", body, "Sil", "Vazgeç") != 0) return;

        try
        {
            CategoryService.Delete(S.Config, S.Store, S.Sorter, category);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageDialog.Info(Owner, "Kategori silinemedi", "Bazı öğeler taşınamadı; kategori yerinde bırakıldı.\n\n" + ex.Message);
        }
        AfterChange(offerReapply: false);
    }

    private void Up_Click(object sender, RoutedEventArgs e) => Shift(sender, -1);

    private void Down_Click(object sender, RoutedEventArgs e) => Shift(sender, +1);

    private void Shift(object sender, int delta)
    {
        CategoryService.Shift(S.Config, S.Store, CategoryOf(sender), delta);
        AfterChange(offerReapply: false);
    }

    private void AfterChange(bool offerReapply)
    {
        S.NotifyChanged();
        Refresh();
        if (!offerReapply) return;

        var plan = CategoryService.PlanReapply(S.Config, S.Sorter);
        if (plan.Count > 0) AskAndApply(plan, "Raf'ta bu kurallara uyan öğeler var");
    }

    private void Reapply_Click(object sender, RoutedEventArgs e)
    {
        var plan = CategoryService.PlanReapply(S.Config, S.Sorter);
        if (plan.Count == 0)
        {
            MessageDialog.Info(Owner, "Her şey yerinde", "Raf'taki öğeler şu anki kurallara göre zaten doğru kategorilerde.");
            return;
        }
        AskAndApply(plan, "Kurallar mevcut dosyalara uygulansın mı?");
    }

    private async void AskAndApply(List<(string Path, Category Target)> plan, string title)
    {
        List<string> lines;
        lock (S.Config) lines = plan.Take(PreviewLines).Select(p => $"{Path.GetFileName(p.Path)}  →  {S.Config.PathName(p.Target)}").ToList();
        if (plan.Count > PreviewLines) lines.Add($"… ve {plan.Count - PreviewLines} öğe daha");

        var body = $"{plan.Count} öğe yer değiştirecek:\n\n{string.Join("\n", lines)}\n\nHer taşıma Geçmiş'ten geri alınabilir.";
        if (MessageDialog.Ask(Owner, title, body, "Taşı", "Şimdilik kalsın") != 0) return;

        var errors = await Task.Run(() =>
        {
            var failed = new List<string>();
            foreach (var (path, target) in plan)
            {
                try { S.Sorter.MoveTo(path, target, auto: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }
            return failed;
        });

        Refresh();
        if (errors.Count > 0) MessageDialog.Info(Owner, "Bazı öğeler taşınamadı", string.Join("\n", errors));
    }

    private void TestBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateTest();

    private void UpdateTest()
    {
        var name = TestBox.Text.Trim();
        if (name.Length == 0)
        {
            TestResult.Visibility = Visibility.Collapsed;
            return;
        }

        Category target;
        string targetName;
        lock (S.Config)
        {
            target = RuleEngine.Classify(S.Config.PriorityOrder(), name);
            targetName = S.Config.PathName(target);
        }
        TestIcon.Text = Ui.Glyph(target.Icon);
        TestIcon.Foreground = Ui.BrushOf(target.Color);
        TestName.Text = targetName;
        TestResult.Visibility = Visibility.Visible;
    }
}
