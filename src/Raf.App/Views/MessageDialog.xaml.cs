using System.Windows;
using System.Windows.Controls;

namespace Raf.App.Views;

/// <summary>Uygulamanın görünümüne uyan soru/bilgi penceresi.</summary>
public partial class MessageDialog : ChromeWindow
{
    private int _result = -1;

    internal MessageDialog(string title, string body, string[] buttons)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;

        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i;
            bool isLast = i == buttons.Length - 1;
            var button = new Button
            {
                Content = buttons[i],
                Style = (Style)FindResource(i == 0 ? "BtnPrimary" : "Btn"),
                Margin = new Thickness(i == 0 ? 0 : 8, 0, 0, 0),
                MinWidth = 84,
                IsDefault = i == 0,
                // Esc, son düğmeye basılmış sayılır.
                IsCancel = isLast,
            };
            button.Click += (_, _) =>
            {
                _result = index;
                Close();
            };
            ButtonRow.Children.Add(button);
        }
    }

    /// <summary>
    /// Soruyu gösterir ve basılan düğmenin sırasını döndürür. İlk düğme asıl eylemdir;
    /// son düğme vazgeçmedir ve Esc ile aynı sonucu verir.
    /// </summary>
    public static int Ask(Window? owner, string title, string body, params string[] buttons)
    {
        var dialog = new MessageDialog(title, body, buttons);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        dialog.ShowDialog();
        return dialog._result < 0 ? buttons.Length - 1 : dialog._result;
    }

    public static void Info(Window? owner, string title, string body) => Ask(owner, title, body, "Tamam");
}
