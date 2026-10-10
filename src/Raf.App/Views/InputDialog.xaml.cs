using System.Windows;

namespace Raf.App.Views;

/// <summary>Tek satırlık metin soran pencere (yeniden adlandırma, yeni klasör).</summary>
public partial class InputDialog : ChromeWindow
{
    private readonly Func<string, string?> _apply;

    private InputDialog(string title, string initial, string okText, bool selectStem, Func<string, string?> apply)
    {
        InitializeComponent();
        _apply = apply;
        TitleText.Text = title;
        OkButton.Content = okText;
        Box.Text = initial;

        Loaded += (_, _) =>
        {
            Box.Focus();
            // Dosya adında uzantı seçilmez; yazmaya başlayınca yalnızca ad değişir.
            int dot = selectStem ? initial.LastIndexOf('.') : -1;
            if (dot > 0) Box.Select(0, dot);
            else Box.SelectAll();
        };
    }

    /// <summary>
    /// Metni sorar ve "Tamam"a basılınca <paramref name="apply"/> ile uygular. apply bir hata metni
    /// döndürürse pencere açık kalır ve hata gösterilir; null döndürürse pencere kapanır.
    /// </summary>
    /// <returns>İşlem uygulandıysa true, vazgeçildiyse false.</returns>
    public static bool Ask(Window? owner, string title, string initial, string okText, bool selectStem, Func<string, string?> apply)
    {
        var dialog = new InputDialog(title, initial, okText, selectStem, apply);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var error = _apply(Box.Text.Trim());
        if (error is null)
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
        Box.Focus();
    }
}
