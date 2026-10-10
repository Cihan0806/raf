using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Raf.App.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    private static Services S => App.S;

    private Window? Owner => Window.GetWindow(this);

    private bool _pinKnown;

    public void Refresh()
    {
        InboxPath.Text = S.Inbox;
        RootPath.Text = S.Config.Root;
        HotkeyBox.Text = S.Config.Hotkey;
        HotkeyStatus.Text = "";
        NotifySwitch.IsChecked = S.Config.Notify;
        FullscreenSwitch.IsChecked = S.Config.PauseHotkeyInFullscreen;
        SearchAppsSwitch.IsChecked = S.Config.SearchApps;
        WidgetSwitch.IsChecked = S.Config.ShowWidget;
        AutostartSwitch.IsChecked = Shell.Autostart;

        List<string> folders;
        lock (S.Config) folders = S.Config.WatchFolders.ToList();
        WatchList.ItemsSource = folders;
        AddDownloadsButton.Visibility = S.IsWatching(Shell.Downloads()) ? Visibility.Collapsed : Visibility.Visible;

        RefreshPinned();
    }

    private async void RefreshPinned()
    {
        // Yanıt kabuk iş parçacığından gelir; ilk yanıta kadar düğme gösterilmez, sonra son bilinen durum kalır.
        if (!_pinKnown) PinButton.Visibility = Visibility.Collapsed;
        bool pinned = await Shell.IsPinnedAsync(S.Inbox);
        _pinKnown = true;
        ShowPinned(pinned);
    }

    private void ShowPinned(bool pinned)
    {
        PinButton.Visibility = pinned ? Visibility.Collapsed : Visibility.Visible;
        PinStatus.Text = pinned ? "Hızlı Erişim'e sabitli." : "";
    }

    private void OpenInbox_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(S.Inbox);

    private void OpenRoot_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(S.Config.Root);

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        PinButton.IsEnabled = false;
        bool pinned = await Shell.PinToQuickAccessAsync(S.Inbox);
        PinButton.IsEnabled = true;
        ShowPinned(pinned);
        if (!pinned)
        {
            PinStatus.Text = "Otomatik sabitlenemedi: açılan pencerede “Gelen”e sağ tıkla, “Hızlı Erişim'e sabitle” de.";
            Shell.OpenFolder(S.Config.Root);
        }
    }

    private void AddDownloads_Click(object sender, RoutedEventArgs e) => AddWatch(Shell.Downloads());

    private void Import_Click(object sender, RoutedEventArgs e) => (Owner as MainWindow)?.ImportExisting();

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "İzlenecek klasörü seç" };
        if (dialog.ShowDialog(Owner) == true) AddWatch(dialog.FolderName);
    }

    private void AddWatch(string folder)
    {
        var error = S.Watch(folder);
        if (error is not null) MessageDialog.Info(Owner, "Bu klasör izlenemiyor", error);
        Refresh();
    }

    private void Unwatch_Click(object sender, RoutedEventArgs e)
    {
        S.Unwatch((string)((FrameworkElement)sender).Tag);
        Refresh();
    }

    private void Notify_Click(object sender, RoutedEventArgs e)
    {
        S.Config.Notify = NotifySwitch.IsChecked == true;
        S.Save();
    }

    private void Widget_Click(object sender, RoutedEventArgs e)
    {
        S.Config.ShowWidget = WidgetSwitch.IsChecked == true;
        S.Save();
        S.Widget?.ApplySetting();
    }

    private void SearchApps_Click(object sender, RoutedEventArgs e)
    {
        S.Config.SearchApps = SearchAppsSwitch.IsChecked == true;
        S.Save();
        _ = S.Launchables.RefreshAsync(TimeSpan.Zero);
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        S.Config.PauseHotkeyInFullscreen = FullscreenSwitch.IsChecked == true;
        S.Hotkey.PauseInFullscreen = S.Config.PauseHotkeyInFullscreen;
        S.Save();
    }

    private void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Shell.Autostart = AutostartSwitch.IsChecked == true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            MessageDialog.Info(Owner, "Ayar kaydedilemedi", ex.Message);
        }
        AutostartSwitch.IsChecked = Shell.Autostart;
    }

    // Kutu odaktayken geçerli kısayol devre dışı kalır; yoksa aynı bileşime basmak pencereyi gizlerdi.
    private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        S.Hotkey.Unregister();
        HotkeyStatus.Text = "Yeni tuş bileşimine bas…";
    }

    private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        S.Hotkey.Register(S.Config.Hotkey);
        HotkeyBox.Text = S.Config.Hotkey;
        if (HotkeyStatus.Text.EndsWith('…')) HotkeyStatus.Text = "";
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab or Key.Escape) return;
        e.Handled = true;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;

        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None)
        {
            HotkeyStatus.Text = "Ctrl, Alt, Shift ya da Win ile birlikte bas.";
            return;
        }

        var text = HotkeyManager.Format(modifiers, key);
        if (!S.Hotkey.Register(text))
        {
            HotkeyStatus.Text = $"{text} kullanılamıyor; başka bir program almış olabilir.";
            return;
        }

        // Kutu hâlâ odakta: kayıt yalnızca denemeydi, odak çıkınca yeniden kaydedilecek.
        S.Hotkey.Unregister();
        S.Config.Hotkey = text;
        S.Save();
        HotkeyBox.Text = text;
        HotkeyStatus.Text = "Kaydedildi.";
    }
}
