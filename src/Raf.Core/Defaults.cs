namespace Raf.Core;

/// <summary>İlk açılışta gelen, türe göre ayıran hazır kategoriler ve seçilebilir renk/simge listeleri.</summary>
public static class Defaults
{
    public static readonly string[] Palette =
    {
        "#7C8CF8", "#3E9FE6", "#34B3A0", "#3BB273", "#8BB53C", "#D9A521",
        "#E5734A", "#E0585F", "#E0709B", "#C77DDB", "#9B7BF0", "#8A8F98",
    };

    /// <summary>Simgesi seçilmemiş kategorinin simgesi (klasör).</summary>
    public const string DefaultIcon = "E8B7";

    /// <summary>
    /// Kategori simgesi seçenekleri: Windows'un simge yazı tipindeki (Segoe Fluent Icons / MDL2)
    /// karakter kodları. Sırasıyla: klasör, belge, resim, video, ses, paket, uygulama, bağlantı, kod, oyun,
    /// iş, ev, yıldız, kalp, indirme, bulut, posta, kişiler, alışveriş, takvim,
    /// kamera, dünya, kitaplık, etiket, raptiye, bayrak, kilit, eğitim, fırça, araba,
    /// ayar, ampul, yapboz, harita.
    /// </summary>
    public static readonly string[] Icons =
    {
        "E8B7", "E8A5", "E8B9", "E714", "E8D6", "E7B8", "ECAA", "E71B", "E943", "E7FC",
        "E821", "E80F", "E734", "EB51", "E896", "E753", "E715", "E716", "E719", "E787",
        "E722", "E774", "E8F1", "E8EC", "E718", "E7C1", "E72E", "E7BE", "E771", "E804",
        "E713", "EA80", "EA86", "E707",
    };

    private static readonly Dictionary<string, string> IconsByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Resimler"] = "E8B9",
        ["Videolar"] = "E714",
        ["Müzik"] = "E8D6",
        ["Belgeler"] = "E8A5",
        ["Arşivler"] = "E7B8",
        ["Programlar"] = "ECAA",
        ["Kısayollar"] = "E71B",
        ["Kod"] = "E943",
        ["Diğer"] = "E712",
    };

    /// <summary>Simgesi kayıtlı olmayan (eski ayar dosyasından gelen) kategori için adına göre simge seçer.</summary>
    public static string IconFor(string categoryName) =>
        IconsByName.TryGetValue(categoryName.Trim(), out var icon) ? icon : DefaultIcon;

    public static List<Category> Categories()
    {
        var list = new List<Category>
        {
            Make("Resimler", "#E0709B", ".png .jpg .jpeg .gif .bmp .webp .svg .ico .heic .tif .tiff"),
            Make("Videolar", "#E5734A", ".mp4 .mkv .avi .mov .wmv .webm .flv .m4v"),
            Make("Müzik", "#C77DDB", ".mp3 .wav .flac .ogg .m4a .aac .wma"),
            Make("Belgeler", "#3E9FE6", ".pdf .doc .docx .xls .xlsx .ppt .pptx .txt .rtf .odt .ods .csv .md"),
            Make("Arşivler", "#D9A521", ".zip .rar .7z .tar .gz .bz2 .xz .iso"),
            Make("Programlar", "#3BB273", ".exe .msi .msix .appinstaller .jar"),
            Make("Kısayollar", "#7C8CF8", ".lnk .url"),
            Make("Kod", "#34B3A0", ".py .js .ts .java .cs .html .css .json .xml .yml .yaml .sql .sh .ps1 .bat .cmd .ini .conf .c .cpp .h .php .go .rs"),
            Fallback(),
        };
        return list;
    }

    public static Category Fallback() => new()
    {
        Name = "Diğer",
        Folder = "Diğer",
        Color = "#8A8F98",
        Icon = IconFor("Diğer"),
        IsFallback = true,
    };

    private static Category Make(string name, string color, string extensions) => new()
    {
        Name = name,
        Folder = name,
        Color = color,
        Icon = IconFor(name),
        Extensions = TextUtil.ParseExtensions(extensions),
    };
}
