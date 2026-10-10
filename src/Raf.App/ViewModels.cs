using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Raf.Core;

namespace Raf.App;

/// <summary>
/// Kenar çubuğundaki bir satır. IsHeader ise yalnızca başlık yazısıdır, seçilemez.
/// Kategori satırlarında simge kategorinin rengiyle (Dot), diğerlerinde soluk renkle çizilir.
/// </summary>
public sealed class NavVM
{
    public string Key { get; init; } = "";
    public string Title { get; init; } = "";
    public string Glyph { get; init; } = "";
    public Brush? Dot { get; init; }
    public string CountText { get; init; } = "";
    public bool IsHeader { get; init; }
    public Category? Category { get; init; }

    /// <summary>Alt kategoriler üst kategorinin altında içeriden başlar.</summary>
    public Thickness Indent { get; init; }

    /// <summary>Raf açılınca gelen sekme olarak işaretlenmiş mi?</summary>
    public bool IsDefault { get; init; }

    public Visibility DefaultVisibility => IsDefault ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HeaderVisibility => IsHeader ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RowVisibility => IsHeader ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DotVisibility => Dot is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GlyphVisibility => Dot is null ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Öğe listesindeki bir grup başlığı.</summary>
public sealed class GroupVM
{
    public string Title { get; init; } = "";
    public string Glyph { get; init; } = "";
    public Brush? Dot { get; init; }
    public string CountText { get; init; } = "";

    /// <summary>"Tümünü göster"e basınca gidilecek kenar çubuğu satırı.</summary>
    public string Key { get; init; } = "";

    public Visibility MoreVisibility { get; init; } = Visibility.Collapsed;
    public Visibility DotVisibility => Dot is null ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>Öğe listesindeki bir kutucuk ya da ayrıntı satırı.</summary>
public sealed class ItemVM : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _requested;

    public ItemVM(LibraryItem item, GroupVM group, string categoryName, DateTime added)
    {
        Item = item;
        Group = group;
        CategoryName = categoryName;
        Added = added;

        // Kısayollarda uzantı kalabalık yapar: "Steam.lnk" yerine "Steam".
        var ext = Path.GetExtension(item.Name);
        bool isShortcut = !item.IsDirectory && (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase));
        Name = isShortcut ? Path.GetFileNameWithoutExtension(item.Name) : item.Name;

        TypeText = item.IsDirectory ? "Klasör"
                 : isShortcut ? "Kısayol"
                 : ext.Length > 1 ? ext[1..].ToUpperInvariant() + " dosyası"
                 : "Dosya";
        SizeText = item.IsDirectory ? "" : Ui.SizeText(item.Size);
        DateText = Ui.WhenText(added);
    }

    public LibraryItem Item { get; }
    public GroupVM Group { get; }
    public string Name { get; }
    public string CategoryName { get; }
    public string CategoryGlyph { get; init; } = "";
    public Brush? CategoryDot { get; init; }
    public DateTime Added { get; }
    public string TypeText { get; }
    public string SizeText { get; }
    public string DateText { get; }

    /// <summary>İçerik aramasında bulunduysa, eşleşmenin geçtiği satır.</summary>
    public string Snippet { get; init; } = "";

    public Visibility LinkVisibility => Item.IsLink ? Visibility.Visible : Visibility.Collapsed;

    public string Tip
    {
        get
        {
            var details = new List<string>();
            if (CategoryName.Length > 0) details.Add(CategoryName);
            details.Add(Item.IsDirectory ? "klasör" : SizeText);
            details.Add(DateText);

            var tip = Item.Name + "\n" + string.Join("  ·  ", details) + "\n"
                    + (Item.IsLink ? "Bağlantı, asıl yeri: " : "") + Item.Path;
            return Snippet.Length > 0 ? tip + "\n\nİçinde: " + Snippet : tip;
        }
    }

    public ImageSource? Icon
    {
        get
        {
            if (_requested) return _icon;
            _requested = true;

            // Dosya aynı adla değiştirilirse (boyutu ya da tarihi değişir) küçük resmi yeniden üretilir.
            long stamp = Item.Size ^ Item.Added.Ticks;
            if (IconLoader.TryGet(Item.Path, Item.IsDirectory, stamp, out _icon)) return _icon;

            IconLoader.Request(Item.Path, Item.IsDirectory, stamp, icon =>
            {
                _icon = icon;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            });
            return null;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Hızlı aramada, Raf'ta olmayan bir sonuç: kurulu uygulama, sistem yeri ya da masaüstündeki bir öğe.</summary>
public sealed class LaunchVM : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private bool _requested;

    public LaunchVM(Launchable item)
    {
        Item = item;
        (CategoryName, CategoryGlyph) = item.Kind switch
        {
            LaunchKind.App => ("Uygulama", Ui.Glyph("ECAA")),
            LaunchKind.Place => ("Sistem", Ui.Glyph("E770")),
            _ => ("Masaüstü", Ui.Glyph("E7F4")),
        };
    }

    public Launchable Item { get; }
    public string Name => Item.Name;
    public string CategoryName { get; }
    public string CategoryGlyph { get; }
    public Brush CategoryDot { get; } = Ui.BrushOf(null);

    public ImageSource? Icon
    {
        get
        {
            if (_requested) return _icon;
            _requested = true;

            // Masaüstündekiler gerçek dosyadır (resimse küçük resmi olur); uygulamalar ve sistem yerleri kabuk öğesidir.
            if (Item.Kind == LaunchKind.Desktop)
            {
                if (IconLoader.TryGet(Item.IconSource, Item.IsDirectory, 0, out _icon)) return _icon;
                IconLoader.Request(Item.IconSource, Item.IsDirectory, 0, Ready);
            }
            else
            {
                if (IconLoader.TryGetShellItem(Item.IconSource, out _icon)) return _icon;
                IconLoader.RequestShellItem(Item.IconSource, Ready);
            }
            return null;
        }
    }

    private void Ready(ImageSource? icon)
    {
        _icon = icon;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Hızlı aramada Raf'ın kendisini temsil eden satır: seçilince tam pencere, varsayılan sekmesiyle açılır.</summary>
public sealed class ActionVM
{
    private static readonly ImageSource Logo = LoadLogo();

    public ActionVM(string name, string detail, Action run)
    {
        Name = name;
        CategoryName = detail;
        Run = run;
    }

    public string Name { get; }
    public string CategoryName { get; }
    public string CategoryGlyph { get; } = Ui.Glyph("E8A7");
    public Brush CategoryDot { get; } = Ui.BrushOf(null);
    public ImageSource Icon => Logo;
    public Action Run { get; }

    private static ImageSource LoadLogo()
    {
        var image = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/raf.png"));
        image.Freeze();
        return image;
    }
}
