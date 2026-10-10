// Bir listedeki öğeleri toplu olarak Raf'a alır (masaüstünü içeri almak gibi tek seferlik işler için).
//
// Kullanım:
//   dotnet run --project tools/Raf.Aktar -- <liste.txt> [--dene]
//   dotnet run --project tools/Raf.Aktar -- <liste.txt> --baglanti <Kategori adı> [--dene]
//
// Liste dosyasında satır başına bir yol bulunur (UTF-8).
//
// Taşıma (varsayılan): her öğenin kategorisi Raf'ın kurallarıyla bulunur; taşıma geçmişe "elle
// yapıldı" diye yazılır, yani Raf'ın Geçmiş sayfasından geri alınabilir ve "kuralları yeniden
// uygula" bu öğelere dokunmaz.
//
// Bağlantı (--baglanti): öğeler yerinden oynatılmaz; verilen kategoride bağlantı olarak gösterilir.
// Kategori yoksa kuralsız olarak ("Diğer"in hemen üstüne) açılır. Proje klasörleri gibi yolu
// değişmemesi gerekenler için.
//
// --dene verilirse hiçbir şey değişmez, yalnızca ne olacağı yazılır.
//
// Raf çalışırken değişiklik yapılmaz: Raf ayarları ve geçmişi bellekte tutar, bu aracın
// yazdıklarının üstüne yazardı.

using System.Diagnostics;
using System.Text;
using Raf.Core;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Kullanım: Raf.Aktar <liste.txt> [--baglanti <Kategori adı>] [--dene]");
    return 2;
}
bool dryRun = args.Contains("--dene");
int linkFlag = Array.IndexOf(args, "--baglanti");
string? linkCategoryName = linkFlag >= 0 && linkFlag + 1 < args.Length ? args[linkFlag + 1] : null;
if (linkFlag >= 0 && string.IsNullOrWhiteSpace(linkCategoryName))
{
    Console.Error.WriteLine("--baglanti seçeneğinden sonra kategori adı gerekir.");
    return 2;
}

if (!dryRun && Process.GetProcessesByName("Raf").Length > 0)
{
    Console.Error.WriteLine("Raf çalışıyor. Önce Raf'ı kapat (tepsi simgesi > Çıkış), sonra yeniden dene.");
    return 3;
}

var store = new ConfigStore();
var cfg = store.Load();
RafLayout.EnsureFolders(cfg);
var sorter = new Sorter(cfg, store);
var paths = File.ReadAllLines(args[0], Encoding.UTF8).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

return linkCategoryName is null ? Move() : Link(linkCategoryName);

int Move()
{
    var perCategory = new SortedDictionary<string, int>(StringComparer.CurrentCulture);
    int moved = 0, skipped = 0, failed = 0;

    foreach (var path in paths)
    {
        var name = Path.GetFileName(RafLayout.Normalize(path));
        if (RafLayout.WhyNotMovable(cfg, path) is { } why)
        {
            Console.WriteLine($"atlandı   {name}: {why}");
            skipped++;
            continue;
        }

        try
        {
            var category = sorter.Classify(path);
            var categoryName = cfg.PathName(category);
            if (!dryRun && sorter.MoveTo(path, category) is null)
            {
                Console.WriteLine($"atlandı   {name}: zaten {categoryName} içinde");
                skipped++;
                continue;
            }

            Console.WriteLine($"{(dryRun ? "gidecek" : "taşındı")}   {name}  →  {categoryName}");
            perCategory[categoryName] = perCategory.GetValueOrDefault(categoryName) + 1;
            moved++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"HATA      {name}: {e.Message}");
            failed++;
        }
    }

    Console.WriteLine();
    Console.WriteLine($"{(dryRun ? "Taşınacak" : "Taşınan")}: {moved}   Atlanan: {skipped}   Hata: {failed}");
    foreach (var (category, count) in perCategory) Console.WriteLine($"  {category}: {count}");
    return failed > 0 ? 1 : 0;
}

int Link(string categoryName)
{
    var category = cfg.Categories.FirstOrDefault(c => TextUtil.Fold(c.Name) == TextUtil.Fold(categoryName));
    bool isNew = category is null;
    if (isNew && !dryRun)
    {
        category = CategoryService.Add(cfg, store, new Category { Name = categoryName, Color = "#D9A521" });
        // Yeni kategoriler en üste eklenir; kuralı olmayan bir bağlantı kategorisi listenin sonunda, "Diğer"in üstünde dursun.
        cfg.Categories.Remove(category);
        cfg.Categories.Insert(cfg.Categories.IndexOf(cfg.Fallback), category);
        store.Save(cfg);
    }
    if (isNew) Console.WriteLine($"“{categoryName}” kategorisi {(dryRun ? "açılacak" : "açıldı")}.");

    int linked = 0, skipped = 0;
    foreach (var path in paths)
    {
        var name = Path.GetFileName(RafLayout.Normalize(path));
        if (!RafLayout.Exists(path))
        {
            Console.WriteLine($"atlandı    {name}: bulunamadı");
            skipped++;
            continue;
        }
        if (RafLayout.IsInside(path, cfg.Root))
        {
            Console.WriteLine($"atlandı    {name}: zaten Raf'ın içinde");
            skipped++;
            continue;
        }

        if (!dryRun) CategoryService.AddLink(cfg, store, path, category!);
        Console.WriteLine($"{(dryRun ? "bağlanacak" : "bağlandı")}   {name}  →  {categoryName}");
        linked++;
    }

    Console.WriteLine();
    Console.WriteLine($"{(dryRun ? "Bağlanacak" : "Bağlanan")}: {linked}   Atlanan: {skipped}");
    return 0;
}
