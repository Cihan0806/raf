using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Raf.App;

/// <summary>
/// Dosya simgelerini arayüzü bekletmeden, ayrı bir iş parçacığında yükler.
/// Resim ve videolarda Dosya Gezgini'nin gösterdiği küçük resmin aynısı, diğerlerinde
/// Windows'un 48 piksellik simgesi kullanılır.
/// </summary>
internal static class IconLoader
{
    private const int ThumbnailSize = 128;
    private const int LargeSize = 256;

    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly BlockingCollection<Job> Queue = new();

    // Windows'un küçük resim üretebildiği türler. Bunlarda her dosyanın görüntüsü farklıdır.
    private static readonly HashSet<string> VisualExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif", ".webp", ".heic", ".heif", ".avif", ".tif", ".tiff", ".ico", ".svg", ".psd",
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".flv", ".mpg", ".mpeg",
    };

    // Windows küçük resim veremezse WPF'in kendi çözücüsüyle okunabilenler.
    private static readonly HashSet<string> DecodableExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".jfif", ".bmp", ".gif" };

    // Simgesi dosyadan dosyaya değişen türler; diğerlerinde uzantı başına tek simge yeter.
    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase) { ".exe", ".lnk", ".url" };

    private const int ShellItemSize = 64;
    private const string ShellItemKeyPrefix = "kabuk|";

    private sealed record Job(string Path, bool IsDirectory, string Key, Action<ImageSource?> Done, bool Large = false, bool ShellItem = false);

    static IconLoader()
    {
        // Kabuk simge arabirimleri tek iş parçacıklı (STA) çalışmayı bekler.
        var worker = new Thread(Work) { IsBackground = true, Name = "Raf.Simge" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <param name="stamp">Dosyanın sürüm damgası (boyut, tarih); dosya değişince küçük resim yenilensin diye.</param>
    public static bool TryGet(string path, bool isDirectory, long stamp, out ImageSource? icon) =>
        Cache.TryGetValue(KeyFor(path, isDirectory, stamp), out icon);

    /// <summary>Simge hazır olunca done çağrılır (arka plan iş parçacığından).</summary>
    public static void Request(string path, bool isDirectory, long stamp, Action<ImageSource?> done) =>
        Queue.Add(new Job(path, isDirectory, KeyFor(path, isDirectory, stamp), done));

    /// <summary>
    /// Önizleme bölmesi için büyük görüntü ister: küçük resmi olan dosyada küçük resim, olmayanda
    /// büyük simge. Bellekte tutulmaz; her istekte yeniden üretilir.
    /// </summary>
    public static void RequestLarge(string path, Action<ImageSource?> done) =>
        Queue.Add(new Job(path, IsDirectory: false, Key: "", done, Large: true));

    /// <summary>
    /// Dosya yolu olmayan bir kabuk öğesinin (ör. "shell:AppsFolder\..." adlı bir uygulamanın ya da
    /// "Bu bilgisayar"ın) simgesi bellekte hazırsa verir.
    /// </summary>
    public static bool TryGetShellItem(string name, out ImageSource? icon) =>
        Cache.TryGetValue(ShellItemKeyPrefix + name, out icon);

    /// <summary>Bir kabuk öğesinin simgesini ister; hazır olunca done çağrılır (arka plan iş parçacığından).</summary>
    public static void RequestShellItem(string name, Action<ImageSource?> done) =>
        Queue.Add(new Job(name, IsDirectory: false, ShellItemKeyPrefix + name, done, ShellItem: true));

    private static string KeyFor(string path, bool isDirectory, long stamp)
    {
        if (isDirectory) return path;
        var ext = Path.GetExtension(path);
        if (VisualExtensions.Contains(ext)) return $"{path}|{stamp}";
        if (PerFileExtensions.Contains(ext)) return path;
        return ext.Length == 0 ? "<uzantisiz>" : ext;
    }

    private static void Work()
    {
        foreach (var job in Queue.GetConsumingEnumerable())
        {
            if (job.Large)
            {
                ImageSource? large = null;
                try { large = ShellImage(job.Path, LargeSize, thumbnailOnly: false); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or COMException or ArgumentException)
                {
                }
                job.Done(large);
                continue;
            }

            if (!Cache.TryGetValue(job.Key, out var icon))
            {
                try { icon = Load(job); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or COMException or ArgumentException)
                {
                    icon = null;
                }
                Cache[job.Key] = icon;
            }
            job.Done(icon);
        }
    }

    private static ImageSource? Load(Job job)
    {
        if (job.ShellItem) return ShellImage(job.Path, ShellItemSize, thumbnailOnly: false);

        var ext = Path.GetExtension(job.Path);
        if (!job.IsDirectory && VisualExtensions.Contains(ext))
        {
            if (ShellImage(job.Path, ThumbnailSize, thumbnailOnly: true) is { } thumbnail) return thumbnail;
            if (DecodableExtensions.Contains(ext))
            {
                try { return DecodedThumbnail(job.Path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
                {
                    // Bozuk ya da okunamayan resim: tür simgesine düş.
                }
            }
            // Küçük resmi olmayan görsel dosya (ör. .ico): dosyanın kendi simgesi.
            return ShellIcon(job.Path, isDirectory: false, attributesOnly: false);
        }

        bool perFile = job.IsDirectory || PerFileExtensions.Contains(ext);
        return ShellIcon(perFile ? job.Path : "x" + ext, job.IsDirectory, attributesOnly: !perFile);
    }

    /// <summary>
    /// Dosya Gezgini'nin gösterdiği görüntüyü ister. thumbnailOnly ise yalnızca küçük resim kabul edilir
    /// (Windows üretemiyorsa null döner); değilse küçük resim yoksa o boyutta simge gelir.
    /// </summary>
    private static ImageSource? ShellImage(string path, int pixels, bool thumbnailOnly)
    {
        var id = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref id, out var factory) != 0 || factory is null) return null;

        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var size = new SIZE { cx = pixels, cy = pixels };
            if (factory.GetImage(size, thumbnailOnly ? SIIGBF_THUMBNAILONLY : 0, out bitmap) != 0 || bitmap == IntPtr.Zero) return null;
            return FromBitmapHandle(bitmap);
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            Marshal.ReleaseComObject(factory);
        }
    }

    /// <summary>
    /// 32 bitlik bir GDI bitmap'ini saydamlığını koruyarak WPF görüntüsüne çevirir.
    /// (Imaging.CreateBitmapSourceFromHBitmap saydam alanları siyaha boyar.)
    /// </summary>
    private static ImageSource FromBitmapHandle(IntPtr bitmap)
    {
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), out var info) == 0 || info.bmBitsPixel != 32)
            return WithoutAlpha(bitmap);

        int width = info.bmWidth;
        int height = info.bmHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];

        // Satırlar GetDIBits ile, açıkça "üstten alta" istenerek okunur (eksi yükseklik). Bitmap'in
        // belleğinden doğrudan kopyalamak güvenilir değil: GetObject yönü her zaman "alttan üste"
        // bildiriyor, oysa Windows'un küçük resimleri bellekte üstten alta duruyor.
        var header = new BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };
        IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref header, 0) != height) return WithoutAlpha(bitmap);
        }
        finally
        {
            DeleteDC(dc);
        }

        // Saydamlık bilgisi taşımayan küçük resimlerde alfa baytları sıfır gelir; opak say.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { hasAlpha = true; break; }
        }
        if (!hasAlpha)
        {
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        source.Freeze();
        return source;
    }

    private static ImageSource WithoutAlpha(IntPtr bitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }

    private static ImageSource DecodedThumbnail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = ThumbnailSize;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static ImageSource? ShellIcon(string path, bool isDirectory, bool attributesOnly)
    {
        var info = new SHFILEINFO();
        uint flags = SHGFI_SYSICONINDEX | (attributesOnly ? SHGFI_USEFILEATTRIBUTES : 0);
        uint attributes = isDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags) == IntPtr.Zero) return null;

        var id = typeof(IImageList).GUID;
        if (SHGetImageList(SHIL_EXTRALARGE, ref id, out var list) != 0 || list is null) return null;

        IntPtr handle = IntPtr.Zero;
        try
        {
            if (list.GetIcon(info.iIcon, ILD_TRANSPARENT, out handle) != 0 || handle == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (handle != IntPtr.Zero) DestroyIcon(handle);
        }
    }

    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    private const int SHIL_EXTRALARGE = 2;
    private const int ILD_TRANSPARENT = 1;
    private const int SIIGBF_THUMBNAILONLY = 0x08;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    // Yalnızca GetIcon kullanılıyor; ondan önceki üyeler sanal tablo sırasını korumak için duruyor.
    [ComImport, Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr image, IntPtr mask, ref int index);
        [PreserveSig] int ReplaceIcon(int index, IntPtr icon, ref int result);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int index, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int mask, ref int index);
        [PreserveSig] int Draw(IntPtr drawParams);
        [PreserveSig] int Remove(int index);
        [PreserveSig] int GetIcon(int index, int flags, out IntPtr icon);
    }

    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int list, ref Guid id, out IImageList? result);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid id, out IShellItemImageFactory? factory);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static extern int GetObject(IntPtr handle, int size, out BITMAP info);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint startLine, uint lineCount, [Out] byte[] pixels, ref BITMAPINFOHEADER header, uint usage);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}
