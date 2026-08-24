using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SolarSim.Preview;

public partial class AppConfirmDialog : UserControl, IAppModal
{
    public event Action<bool?>? Completed;

    public AppConfirmDialog(
        string title,
        string heading,
        string body,
        string? footnote,
        string confirmLabel,
        string? cancelLabel)
    {
        InitializeComponent();
        TitleText.Text = title;
        HeadingText.Text = heading;
        ConfirmButton.Content = confirmLabel;

        if (string.IsNullOrWhiteSpace(body))
        {
            BodyBorder.Visibility = Visibility.Collapsed;
        }
        else
        {
            BodyText.Text = body;
        }

        if (string.IsNullOrWhiteSpace(footnote))
            FootnoteText.Visibility = Visibility.Collapsed;
        else
            FootnoteText.Text = footnote;

        if (string.IsNullOrWhiteSpace(cancelLabel))
        {
            CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            CancelButton.Content = cancelLabel;
        }
    }

    public static bool Ask(
        Window? owner,
        string title,
        string heading,
        string body,
        string? footnote = null,
        string confirmLabel = "OK",
        string? cancelLabel = "Cancel")
    {
        _ = owner;
        var dialog = new AppConfirmDialog(title, heading, body, footnote, confirmLabel, cancelLabel);
        return AppModalHost.Show(dialog) == true;
    }

    public static void Tell(
        Window? owner,
        string title,
        string heading,
        string body,
        string? footnote = null)
    {
        Ask(owner, title, heading, body, footnote, confirmLabel: "OK", cancelLabel: null);
    }

    /// <summary>Drop-in for MessageBox.Show — stays on the main window HWND.</summary>
    public static MessageBoxResult Alert(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        _ = image;
        if (button is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel)
        {
            return Ask(owner, caption, message, "", confirmLabel: "Yes", cancelLabel: "No")
                ? MessageBoxResult.Yes
                : MessageBoxResult.No;
        }

        Tell(owner, caption, message, "");
        return MessageBoxResult.OK;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(true);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(false);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Cancel_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            Confirm_Click(sender, e);
            e.Handled = true;
        }
    }
}
