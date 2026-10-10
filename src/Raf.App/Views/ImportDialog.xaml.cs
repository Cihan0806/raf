using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using Microsoft.Win32;
using Raf.Core;

namespace Raf.App.Views;

/// <summary>
/// Raf kurulmadan önce birikmiş dosyaları içeri alır: kaynak klasörler seçilir, neyin nereye
/// gideceği gösterilir, onaylanınca uygulanır.
/// </summary>
public partial class ImportDialog : ChromeWindow
{
    public sealed class SourceVM
    {
        public required string Path { get; init; }
        public required string Title { get; init; }
        public required string Detail { get; init; }
        public bool IsChecked { get; set; }
    }

    public sealed class RowVM
    {
        public required string Name { get; init; }
        public required string Path { get; init; }
        public required string Target { get; init; }
        public required string Glyph { get; init; }
        public double Opacity { get; init; } = 1;
    }

    private readonly ObservableCollection<SourceVM> _sources = new();
    private List<ImportEntry> _plan = new();
    private int _planVersion;
    private bool _busy;

    private static Services S => App.S;

    /// <summary>İçeri alma yapıldıysa sonucu; vazgeçildiyse null.</summary>
    public ImportResult? Result { get; private set; }

    /// <param name="folders">Verilirse kaynak olarak yalnızca bunlar sunulur; verilmezse masaüstü ve bilinen klasörler.</param>
    internal ImportDialog(IReadOnlyList<string>? folders = null)
    {
        InitializeComponent();
        // Küçük ekranlarda pencere çalışma alanını aşmasın; içerik kaydırılır.
        MaxHeight = SystemParameters.WorkArea.Height - 24;

        if (folders is not null)
        {
            foreach (var folder in folders) AddSource(folder, FolderTitle(folder), isChecked: true);
        }
        else
        {
            AddSource(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Masaüstü", isChecked: true);
            AddSource(Shell.Downloads(), "İndirilenler", isChecked: false);
            AddSource(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Belgeler", isChecked: false);
            AddSource(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Resimler", isChecked: false);
            AddSource(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Ortak masaüstü", isChecked: false,
                detail: "Bu bilgisayardaki bütün kullanıcıların masaüstünde görünen kısayollar");
        }
        SourceList.ItemsSource = _sources;

        Loaded += (_, _) => Replan();
        Closing += OnClosing;
    }

    /// <summary>Pencereyi gösterir; içeri alma yapıldıysa sonucunu döndürür.</summary>
    public static ImportResult? Show(Window owner, IReadOnlyList<string>? folders = null, bool activate = true)
    {
        var dialog = new ImportDialog(folders) { Owner = owner, ShowActivated = activate };
        dialog.ShowDialog();
        return dialog.Result;
    }

    private static string FolderTitle(string folder) =>
        Path.GetFileName(RafLayout.Normalize(folder)) is { Length: > 0 } name ? name : folder;

    private void AddSource(string path, string title, bool isChecked, string? detail = null)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (_sources.Any(s => RafLayout.IsSame(s.Path, path))) return;
        _sources.Add(new SourceVM { Path = path, Title = title, Detail = detail ?? path, IsChecked = isChecked });
    }

    private void Option_Click(object sender, RoutedEventArgs e) => Replan();

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "İçeri alınacak klasörü seç" };
        if (dialog.ShowDialog(this) != true) return;

        var folder = dialog.FolderName;
        string root;
        lock (S.Config) root = S.Config.Root;
        if (RafLayout.IsInside(folder, root))
        {
            MessageDialog.Info(this, "Bu klasör zaten Raf'ın içinde", "Raf'ın kendi klasöründekiler zaten kategorilerinde duruyor.");
            return;
        }

        if (_sources.FirstOrDefault(s => RafLayout.IsSame(s.Path, folder)) is { } existing)
        {
            existing.IsChecked = true;
            SourceList.Items.Refresh();
        }
        else
        {
            AddSource(folder, FolderTitle(folder), isChecked: true);
        }
        Replan();
    }

    /// <summary>Seçime göre neyin nereye gideceğini arka planda yeniden çıkarır.</summary>
    private async void Replan()
    {
        int version = ++_planVersion;
        var folders = _sources.Where(s => s.IsChecked).Select(s => s.Path).ToList();
        bool linkFolders = FoldersSwitch.IsChecked == true;

        ImportButton.IsEnabled = false;
        SummaryTitle.Text = "Bakılıyor…";
        SummaryText.Text = "";

        var plan = await Task.Run(() => Importer.Plan(S.Config, S.Sorter, folders, linkFolders));
        if (version != _planVersion) return;

        _plan = plan;
        ShowPlan(folders.Count);
    }

    private void ShowPlan(int folderCount)
    {
        var active = _plan.Where(e => e.Action != ImportAction.Skip).ToList();
        int skipped = _plan.Count - active.Count;

        PreviewList.ItemsSource = _plan
            .OrderBy(e => e.Action == ImportAction.Skip)
            .Select(e => new RowVM
            {
                Name = e.Name,
                Path = e.Path,
                Glyph = Ui.Glyph(e.IsDirectory ? "E8B7" : "E8A5"),
                Target = e.Action switch
                {
                    ImportAction.Move => "→  " + e.TargetName,
                    ImportAction.Link => "bağlantı  →  " + e.TargetName,
                    _ => "atlanacak: " + e.Reason,
                },
                Opacity = e.Action == ImportAction.Skip ? 0.55 : 1,
            })
            .ToList();

        if (folderCount == 0)
        {
            SummaryTitle.Text = "Bir klasör seç";
            SummaryText.Text = "İçeri alınacak dosyaların durduğu klasörü yukarıdan işaretle.";
        }
        else if (active.Count == 0)
        {
            SummaryTitle.Text = "İçeri alınacak bir şey yok";
            SummaryText.Text = skipped > 0 ? $"{skipped} öğe atlanacak." : "Seçtiğin klasörlerde içeri alınabilecek dosya ya da klasör bulunamadı.";
        }
        else
        {
            SummaryTitle.Text = $"{active.Count} öğe içeri alınacak";
            var parts = active
                .GroupBy(e => e.TargetName)
                .OrderByDescending(g => g.Count())
                .Select(g => $"{g.Key} {g.Count()}" + (g.First().Action == ImportAction.Link ? " (bağlantı)" : ""));
            SummaryText.Text = string.Join("   ·   ", parts) + (skipped > 0 ? $"\n{skipped} öğe atlanacak." : "");
        }

        ImportButton.IsEnabled = active.Count > 0;
        StatusText.Text = "";
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var plan = _plan;
        int total = plan.Count(entry => entry.Action != ImportAction.Skip);
        if (_busy || total == 0) return;

        _busy = true;
        _planVersion++;
        Options.IsEnabled = false;
        ImportButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        StatusText.Text = $"İçeri alınıyor…  0 / {total}";

        var progress = new Progress<int>(done => StatusText.Text = $"İçeri alınıyor…  {done} / {total}");
        Result = await Task.Run(() => Importer.Apply(S.Config, S.Store, S.Sorter, plan, ((IProgress<int>)progress).Report));

        _busy = false;
        Close();
    }

#if DEBUG
    /// <summary>Geliştirme yardımcısı: "İçeri al" düğmesine basılmış gibi davranır.</summary>
    internal void DebugImport() => Import_Click(this, new RoutedEventArgs());
#endif

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Taşıma sürerken pencere kapanmaz; yarıda kesilirse neyin taşındığı belirsiz kalırdı.
        if (_busy) e.Cancel = true;
    }
}
