using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Raf.App;

/// <summary>Raf başka bir pencere öndeyken de çalışan genel kısayol tuşu.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const int HotkeyId = 0x5241;
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;
    private static readonly IntPtr MessageOnlyParent = new(-3);

    private readonly HwndSource _source;
    private readonly DispatcherTimer _fullscreenTimer;
    private string? _wanted;
    private bool _registered;

    public HotkeyManager()
    {
        _source = new HwndSource(new HwndSourceParameters("Raf.Kisayol") { ParentWindow = MessageOnlyParent, WindowStyle = 0 });
        _source.AddHook(Hook);

        // Öndeki pencere değişince hemen, ayrıca (öndeki pencere yerinde tam ekrana geçebildiği için)
        // iki saniyede bir kontrol edilir.
        FrontWindow.Changed += FollowFullscreen;
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fullscreenTimer.Tick += (_, _) => FollowFullscreen();
        _fullscreenTimer.Start();
    }

    public event Action? Pressed;

    /// <summary>
    /// Açıkken, tam ekran bir oyun ya da video öndeyken kısayol bırakılır; böylece tuş bileşimi
    /// oyuna ulaşır ve Raf oyunun ortasında açılmaz. Başka bir pencereye geçilince kısayol geri gelir
    /// (oyun arkada ya da başka bir ekranda açık kalsa bile).
    /// </summary>
    public bool PauseInFullscreen { get; set; } = true;

    /// <summary>"Alt+Space" biçimindeki kısayolu kaydeder. Başka bir program kullanıyorsa false döner.</summary>
    public bool Register(string text)
    {
        Unregister();
        if (!TryParse(text, out _, out _)) return false;
        _wanted = text;
        // Şu an tam ekran bir oyun öndeyse hiç kaydetme; çıkılınca FollowFullscreen kaydeder.
        if (PauseInFullscreen && FrontWindow.IsFullscreenApp()) return true;
        return TryRegister();
    }

    public void Unregister()
    {
        _wanted = null;
        Release();
    }

    private bool TryRegister()
    {
        if (_wanted is null || !TryParse(_wanted, out var modifiers, out var key)) return false;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, (uint)modifiers | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    private void Release()
    {
        if (_registered) UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private void FollowFullscreen()
    {
        if (_wanted is null) return;

        bool pause = PauseInFullscreen && FrontWindow.IsFullscreenApp();
        if (pause && _registered) Release();
        else if (!pause && !_registered) TryRegister();
    }

    public static bool TryParse(string text, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;
        foreach (var part in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win": modifiers |= ModifierKeys.Windows; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        return key != Key.None && modifiers != ModifierKeys.None;
    }

    public static string Format(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _fullscreenTimer.Stop();
        FrontWindow.Changed -= FollowFullscreen;
        Unregister();
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
