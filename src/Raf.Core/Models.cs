using System.Text.Json.Serialization;

namespace Raf.Core;

/// <summary>Bir kategori ve o kategoriye hangi dosyaların gideceğini söyleyen kurallar.</summary>
public sealed class Category
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";

    /// <summary>
    /// Bağlı olduğu üst kategorinin kimliği; boşsa ana kategoridir. Yalnızca bir seviye iç içe olunur:
    /// alt kategorinin alt kategorisi olmaz.
    /// </summary>
    public string ParentId { get; set; } = "";

    /// <summary>Klasör adı: ana kategoride Raf klasörünün, alt kategoride üst kategori klasörünün içinde.</summary>
    public string Folder { get; set; } = "";

    public string Color { get; set; } = "#7C8CF8";

    /// <summary>Simge yazı tipindeki karakterin onaltılık kodu, ör. "E8B9". Seçenekler: <see cref="Defaults.Icons"/>.</summary>
    public string Icon { get; set; } = "";

    public List<string> Extensions { get; set; } = new();
    public List<string> NameContains { get; set; } = new();
    public List<string> Sites { get; set; } = new();

    /// <summary>true ise doldurulan bütün ölçütler aynı anda uymalı; false ise biri yeter.</summary>
    public bool MatchAll { get; set; }

    /// <summary>Hiçbir kurala uymayanların düştüğü kategori. Tam olarak bir tane bulunur.</summary>
    public bool IsFallback { get; set; }

    [JsonIgnore]
    public bool HasRules => Extensions.Count > 0 || NameContains.Count > 0 || Sites.Count > 0;
}

/// <summary>Yerinden oynatılmadan bir kategoride gösterilen öğe.</summary>
public sealed class LinkItem
{
    public string Path { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public DateTime Added { get; set; }
}

public enum SortKey
{
    Name,
    Date,
    Size,
    Type,
}

public sealed class RafConfig
{
    public string Root { get; set; } = "";
    public string InboxFolder { get; set; } = "Gelen";

    /// <summary>Gelen klasörüne ek olarak izlenen klasörler. Buralarda yalnızca yeni gelenlere dokunulur.</summary>
    public List<string> WatchFolders { get; set; } = new();

    /// <summary>
    /// Ana ve alt kategoriler tek listede durur. Listedeki sıra, aynı seviyedeki kategorilerin kendi
    /// aralarındaki sırasıdır; kuralların denenme sırası için <see cref="PriorityOrder"/> kullanılır.
    /// </summary>
    public List<Category> Categories { get; set; } = new();

    public List<LinkItem> Links { get; set; } = new();

    /// <summary>Geri alındığı için otomatik düzenlemenin dokunmayacağı yollar.</summary>
    public List<string> Held { get; set; } = new();

    public string Hotkey { get; set; } = "Alt+Space";

    /// <summary>Tam ekran oyun ya da video öndeyken kısayol tuşu devre dışı kalsın.</summary>
    public bool PauseHotkeyInFullscreen { get; set; } = true;

    public bool Notify { get; set; } = true;

    /// <summary>
    /// Hızlı arama, Raf'taki öğelerin yanında kurulu uygulamaları, sistem klasörlerini ve masaüstündekileri
    /// de göstersin. Bunlar yalnızca listelenir; hiçbiri Raf'a alınmaz ya da yerinden oynatılmaz.
    /// </summary>
    public bool SearchApps { get; set; } = true;

    /// <summary>
    /// Tam pencere açılınca gelen sekmenin kimliği ("all", "recent", "cat:&lt;kategori kimliği&gt;");
    /// boşsa "Tümü". Kenar çubuğunda bir sekmeye sağ tıklanıp "Varsayılan yap" denerek seçilir.
    /// </summary>
    public string DefaultView { get; set; } = "";

    /// <summary>Masaüstünde duran, tıklanınca hızlı aramayı açan küçük "Uygulama ara" kutucuğu gösterilsin.</summary>
    public bool ShowWidget { get; set; } = true;

    /// <summary>Kutucuk masaüstünde durmak yerine diğer pencerelerin de üstünde kalsın.</summary>
    public bool WidgetOnTop { get; set; }

    /// <summary>Kutucuğun ekrandaki yeri; boşsa varsayılan yere (ana ekranın üst ortası) konur.</summary>
    public double? WidgetLeft { get; set; }

    public double? WidgetTop { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SortKey SortBy { get; set; } = SortKey.Date;

    public bool SortDescending { get; set; } = true;

    /// <summary>true ise öğeler kutucuk yerine ayrıntı listesi olarak gösterilir.</summary>
    public bool DetailsView { get; set; }

    public bool ShowPreview { get; set; }

    [JsonIgnore]
    public Category Fallback => Categories.First(c => c.IsFallback);

    public Category? Find(string id) => Categories.FirstOrDefault(c => c.Id == id);

    /// <summary>Kategorinin üst kategorisi; ana kategoriyse (ya da üstü artık yoksa) null.</summary>
    public Category? Parent(Category category)
    {
        if (category.ParentId.Length == 0) return null;
        return Categories.FirstOrDefault(p => p.Id == category.ParentId && p != category && p.ParentId.Length == 0);
    }

    public List<Category> TopLevel() => Categories.Where(c => Parent(c) is null).ToList();

    public List<Category> Children(Category category) =>
        Parent(category) is not null
            ? new List<Category>()
            : Categories.Where(c => c != category && Parent(c) == category).ToList();

    /// <summary>Gösterim sırası: her ana kategori, hemen ardından alt kategorileri.</summary>
    public List<Category> TreeOrder()
    {
        var order = new List<Category>();
        foreach (var top in TopLevel())
        {
            order.Add(top);
            order.AddRange(Children(top));
        }
        return order;
    }

    /// <summary>
    /// Kuralların denenme sırası. Alt kategoriler daha özel olduğu için bağlı oldukları kategoriden
    /// önce denenir; yoksa üst kategorinin kuralı her şeyi kapar, alt kategoriye bir şey kalmazdı.
    /// </summary>
    public List<Category> PriorityOrder()
    {
        var order = new List<Category>();
        foreach (var top in TopLevel())
        {
            order.AddRange(Children(top));
            order.Add(top);
        }
        return order;
    }

    /// <summary>"Minecraft › Modlar" biçiminde tam ad.</summary>
    public string PathName(Category category)
    {
        var parent = Parent(category);
        return parent is null ? category.Name : $"{parent.Name} › {category.Name}";
    }
}

public sealed class MoveRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string CategoryId { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public DateTime Time { get; set; }

    /// <summary>true ise izleme tarafından kurallara göre yapıldı; false ise kullanıcı elle taşıdı.</summary>
    public bool Auto { get; set; }

    /// <summary>true ise öğe yer değiştirmedi, yalnızca adı değişti.</summary>
    public bool Renamed { get; set; }

    public bool Undone { get; set; }
}

public sealed record LibraryItem(
    string Path,
    string Name,
    string CategoryId,
    bool IsDirectory,
    bool IsLink,
    DateTime Added,
    long Size);
