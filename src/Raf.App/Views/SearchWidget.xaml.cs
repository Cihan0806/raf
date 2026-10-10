using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Raf.App.Views;

/// <summary>
/// Masaüstünde duran küçük "Uygulama ara" kutucuğu. Tıklanınca hızlı arama çubuğu kutucuğun
/// olduğu yerde açılır; sürüklenerek taşınır, yeri hatırlanır.
/// Normalde masaüstü seviyesinde durur (pencerelerin altında kalır); masaüstünün kendisi öne
/// geldiğinde (boş masaüstüne tıklanınca ya da "masaüstünü göster" yapılınca) görünür kalması için
/// öne çıkar. İstenirse her zaman üstte de tutulabilir; o zaman tam ekran oyun/video öndeyken gizlenir.
/// </summary>
public partial class SearchWidget : Window
{
    // XAML'deki Grid.Margin ve Pill boyutuyla aynı olmalı.
    private const double MarginLeft = 12;
    private const double MarginTop = 8;
    private const double PillWidth = 208;
    private const double PillHeight = 42;
    private const double WindowWidth = PillWidth + 24;
    private const double WindowHeight = PillHeight + 24;

    /// <summary>İlk açılışta ana ekranda, üstten bu oranda aşağıya ve yatayda ortaya konur (simgelerin genelde olmadığı yer).</summary>
    private const double DefaultTopRatio = 0.4;
    private const double DragThreshold = 4;

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const int WmWindowPosChanging = 0x0046;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);

    private readonly Services _s = App.S;
    private readonly Action<Rect, Action> _openSearch;
    private readonly Action _openFullWindow;
    private readonly DispatcherTimer _layerTimer;
    private IntPtr _hwnd;
    private Point _pressedAt;
    private bool _pressed;
    private bool _dragged;
    private bool _raised;

    /// <param name="openSearch">
    /// Aramayı, verilen ekran dikdörtgeninin (kutucuğun) olduğu yerde açar; arama kapanınca ikinci
    /// parametredeki eylemi çağırır.
    /// </param>
    public SearchWidget(Action<Rect, Action> openSearch, Action openFullWindow)
    {
        InitializeComponent();
        _openSearch = openSearch;
        _openFullWindow = openFullWindow;

        // Öndeki pencere yerinde tam ekrana geçebilir; olay gelmese de durum iki saniyede bir yenilenir.
        _layerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _layerTimer.Tick += (_, _) => ApplyLayer();
    }

    /// <summary>Uygulamadan çıkılırken true yapılır; o zamana kadar kutucuk kapatılamaz.</summary>
    public bool Exiting { get; set; }

    /// <summary>Ayara göre kutucuğu gösterir ya da gizler.</summary>
    public void ApplySetting()
    {
        if (!_s.Config.ShowWidget)
        {
            _layerTimer.Stop();
            Hide();
            return;
        }

        if (_hwnd == IntPtr.Zero) new WindowInteropHelper(this).EnsureHandle();
        Place();
        Show();
        _layerTimer.Start();
        ApplyLayer();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;

        // Araç penceresi: Alt+Tab listesinde ve görev çubuğunda görünmez.
        long style = GetWindowLongPtr(_hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(_hwnd, GwlExStyle, new IntPtr(style | WsExToolWindow));

        HwndSource.FromHwnd(_hwnd)!.AddHook(Hook);
        FrontWindow.Changed += ApplyLayer;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!Exiting) e.Cancel = true;
        else FrontWindow.Changed -= ApplyLayer;
        base.OnClosing(e);
    }

    // ===== Yer =====

    private void Place()
    {
        double? left = _s.Config.WidgetLeft;
        double? top = _s.Config.WidgetTop;
        if (left is null || top is null || !IsOnAScreen(left.Value, top.Value))
        {
            var area = SystemParameters.WorkArea;
            left = area.Left + (area.Width - WindowWidth) / 2;
            top = area.Top + area.Height * DefaultTopRatio;
        }
        Left = left.Value;
        Top = top.Value;
    }

    /// <summary>Kayıtlı yer hâlâ bağlı bir ekranın içinde mi? (Ekran çıkarılmış ya da çözünürlük değişmiş olabilir.)</summary>
    private bool IsOnAScreen(double left, double top)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var center = new System.Drawing.Point(
            (int)((left + WindowWidth / 2) * dpi.DpiScaleX),
            (int)((top + WindowHeight / 2) * dpi.DpiScaleY));
        return WinForms.Screen.AllScreens.Any(screen => screen.Bounds.Contains(center));
    }

    private void SavePosition()
    {
        _s.Config.WidgetLeft = Left;
        _s.Config.WidgetTop = Top;
        _s.Save();
    }

    // ===== Katman: masaüstünde ya da her zaman üstte =====

    private void ApplyLayer()
    {
        if (_hwnd == IntPtr.Zero || !_s.Config.ShowWidget) return;

        if (_s.Config.WidgetOnTop)
        {
            // Oyunun ya da tam ekran videonun üstünde durmasın.
            if (FrontWindow.IsFullscreenApp())
            {
                if (IsVisible) Hide();
                return;
            }
            if (!IsVisible) Show();
            _raised = true;
            SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            return;
        }

        if (!IsVisible) Show();
        // Raf'ın kendi penceresi (arama çubuğu, bu kutucuğun menüsü) öndeyken durum değişmez;
        // yoksa kutucuğa tıklamak onu bulunduğu katmandan oynatırdı.
        if (!FrontWindow.IsOwnProcess()) _raised = FrontWindow.IsDesktop();

        SetWindowPos(_hwnd, _raised ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        if (!_raised) SetWindowPos(_hwnd, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Masaüstü seviyesindeyken tıklanması kutucuğu diğer pencerelerin önüne getirmesin.
        if (message == WmWindowPosChanging && !_raised)
        {
            var position = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((position.flags & SwpNoZOrder) == 0)
            {
                position.hwndInsertAfter = HwndBottom;
                Marshal.StructureToPtr(position, lParam, fDeleteOld: false);
            }
        }
        return IntPtr.Zero;
    }

    // ===== Fare =====

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedAt = e.GetPosition(this);
        _pressed = true;
        _dragged = false;
        Pill.CaptureMouse();
    }

    private void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragged || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _pressedAt.X) < DragThreshold && Math.Abs(now.Y - _pressedAt.Y) < DragThreshold) return;

        // DragMove fare bırakılana kadar döner; bırakma olayı bu yüzden ayrıca gelmez.
        _dragged = true;
        Pill.ReleaseMouseCapture();
        DragMove();
        _pressed = false;
        SavePosition();
    }

    private void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Pill.ReleaseMouseCapture();
        bool clicked = _pressed && !_dragged;
        _pressed = false;
        if (!clicked) return;

        // Arama kutusu kutucuğun tam üstünde, ondan biraz geniş açılır; kutucuk o sırada görünmez olur ki
        // ikisi üst üste durmasın ve kutucuk yerinde büyümüş gibi görünsün.
        Opacity = 0;
        _openSearch(new Rect(Left + MarginLeft, Top + MarginTop, PillWidth, PillHeight), () => Opacity = 1);
    }

    private void Pill_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = Pill };
        menu.Items.Add(Entry("Her zaman üstte", _s.Config.WidgetOnTop ? "E73E" : "", () =>
        {
            _s.Config.WidgetOnTop = !_s.Config.WidgetOnTop;
            _s.Save();
            _raised = false;
            ApplyLayer();
        }));
        menu.Items.Add(Entry("Raf'ı aç", "E8A7", _openFullWindow));
        menu.Items.Add(new Separator());
        menu.Items.Add(Entry("Kutucuğu gizle", "E711", () =>
        {
            // Ayarlar sayfasındaki anahtardan yeniden açılır.
            _s.Config.ShowWidget = false;
            _s.Save();
            ApplySetting();
        }));
        menu.IsOpen = true;
        e.Handled = true;
    }

    private MenuItem Entry(string header, string iconCode, Action action)
    {
        var icon = new TextBlock
        {
            Text = iconCode.Length > 0 ? Ui.Glyph(iconCode) : "",
            Width = 16,
            FontSize = 14,
            Style = (Style)FindResource("Icon"),
        };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");

        var entry = new MenuItem { Header = header, Icon = icon };
        entry.Click += (_, _) => action();
        return entry;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd;
        public IntPtr hwndInsertAfter;
        public int x;
        public int y;
        public int cx;
        public int cy;
        public uint flags;
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
}
