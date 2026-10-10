using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Raf.Core;

namespace Raf.App;

/// <summary>Windows kabuğuyla ilgili işler: açma, klasörde gösterme, Hızlı Erişim, açılışta başlatma.</summary>
internal static class Shell
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "Raf";
    private const string QuickAccessNamespace = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static void Open(string path)
    {
        var info = new ProcessStartInfo(path) { UseShellExecute = true };
        if (File.Exists(path)) info.WorkingDirectory = Path.GetDirectoryName(path) ?? "";
        Process.Start(info);
    }

    public static void Reveal(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Öğeyi Geri Dönüşüm Kutusu'na gönderir. Windows'un kendi pencereleri açık bırakılır: öğe kutuya
    /// sığmayacak kadar büyükse "kalıcı olarak silinsin mi" sorusunu Windows sorar, sessizce silinmez.
    /// </summary>
    /// <exception cref="OperationCanceledException">Kullanıcı Windows'un sorusunda vazgeçtiyse.</exception>
    public static void Recycle(string path)
    {
        if (Directory.Exists(path))
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
        else
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                Microsoft.VisualBasic.FileIO.UIOption.AllDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
    }

    /// <summary>Windows'un standart "Özellikler" penceresini açar.</summary>
    public static bool ShowProperties(string path)
    {
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SeeMaskInvokeIdList,
            lpVerb = "properties",
            lpFile = path,
            nShow = ShowNormal,
        };
        return ShellExecuteEx(ref info);
    }

    /// <summary>Dosyaları panoya koyar; Dosya Gezgini dahil her yere yapıştırılabilir.</summary>
    /// <param name="cut">true ise yapıştırılan yer dosyaları taşır ("Kes").</param>
    public static void CopyFiles(IEnumerable<string> paths, bool cut)
    {
        var files = new System.Collections.Specialized.StringCollection();
        files.AddRange(paths.ToArray());

        var data = new System.Windows.DataObject();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffect, new MemoryStream(BitConverter.GetBytes(cut ? DropEffectMove : DropEffectCopy | DropEffectLink)));
        System.Windows.Clipboard.SetDataObject(data, copy: true);
    }

    /// <summary>Panodaki dosyalar ve kesilerek mi konuldukları. Panoda dosya yoksa boş liste döner.</summary>
    public static (string[] Paths, bool Cut) FilesOnClipboard()
    {
        if (!System.Windows.Clipboard.ContainsFileDropList()) return (Array.Empty<string>(), false);

        var paths = System.Windows.Clipboard.GetFileDropList().Cast<string>().ToArray();
        bool cut = false;
        if (System.Windows.Clipboard.GetData(PreferredDropEffect) is MemoryStream effect && effect.Length >= sizeof(int))
        {
            var bytes = new byte[sizeof(int)];
            effect.Position = 0;
            effect.ReadExactly(bytes);
            cut = (BitConverter.ToInt32(bytes) & DropEffectMove) != 0;
        }
        return (paths, cut);
    }

    private const string PreferredDropEffect = "Preferred DropEffect";
    private const int DropEffectCopy = 1;
    private const int DropEffectMove = 2;
    private const int DropEffectLink = 4;
    private const uint SeeMaskInvokeIdList = 0x0000000C;
    private const int ShowNormal = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string lpVerb;
        public string lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    public static string Downloads()
    {
        if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out var pointer) == 0)
        {
            try { return Marshal.PtrToStringUni(pointer)!; }
            finally { Marshal.FreeCoTaskMem(pointer); }
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    /// <summary>Klasör Dosya Gezgini'nin Hızlı Erişim listesinde mi? (Kaydet pencerelerinin sol paneli.)</summary>
    public static Task<bool> IsPinnedAsync(string folder) => ShellThread.Run(() => IsPinned(folder));

    /// <summary>Klasörü Hızlı Erişim'e sabitler; sonuç listeye yeniden bakılarak doğrulanır.</summary>
    public static Task<bool> PinToQuickAccessAsync(string folder) => ShellThread.Run(() =>
    {
        if (IsPinned(folder)) return true;
        Directory.CreateDirectory(folder);
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            shell.NameSpace(folder).Self.InvokeVerb("pintohome");
        }
        catch (Exception)
        {
            return false;
        }
        return IsPinned(folder);
    });

    // Kabuk iş parçacığında çalışır. Kabuk çağrıları Windows'un hata koduna göre çok farklı türde hata
    // fırlatır; liste okunamadıysa "sabitli değil" sayılır.
    private static bool IsPinned(string folder)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            foreach (dynamic item in shell.NameSpace(QuickAccessNamespace).Items())
            {
                if (item.IsFolder && RafLayout.IsSame((string)item.Path, folder)) return true;
            }
        }
        catch (Exception)
        {
        }
        return false;
    }

    public static bool Autostart
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
            else key.DeleteValue(RunValue, throwOnMissingValue: false);
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
