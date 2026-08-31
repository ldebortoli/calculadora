using System.Windows;

namespace Cashflow.Windows;

public partial class AppDialogWindow : Window
{
    private bool _accepted;

    private AppDialogWindow(string message, string title, string acceptLabel, string? cancelLabel)
    {
        InitializeComponent();
        WindowTheme.ApplyDarkTitleBar(this);
        Title = $"{title} · Calculadora";
        HeaderText.Text = title;
        MessageText.Text = message;
        AcceptButton.Content = acceptLabel;

        if (cancelLabel is null)
        {
            SecondaryButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryButton.Content = cancelLabel;
            AcceptButton.Style = (Style)FindResource("DangerButton");
        }
    }

    public static void ShowInfo(Window? owner, string message, string title)
    {
        var dialog = new AppDialogWindow(message, title, "Aceptar", null);
        ConfigureOwner(dialog, owner);
        dialog.ShowDialog();
    }

    public static bool Confirm(Window? owner, string message, string title, string acceptLabel)
    {
        var dialog = new AppDialogWindow(message, title, acceptLabel, "Cancelar");
        ConfigureOwner(dialog, owner);
        dialog.ShowDialog();
        return dialog._accepted;
    }

    private static void ConfigureOwner(Window dialog, Window? owner)
    {
        if (owner is null)
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        dialog.Owner = owner;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private void AcceptButton_Click(object sender, RoutedEventArgs e)
    {
        _accepted = true;
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e)
    {
        _accepted = false;
        DialogResult = false;
    }
}
