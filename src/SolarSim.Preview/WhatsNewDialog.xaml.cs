using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SolarSim.Preview;

public partial class WhatsNewDialog : UserControl, IAppModal
{
    public event Action<bool?>? Completed;

    public WhatsNewDialog(string version, string? releasedLocal, string notes)
    {
        InitializeComponent();
        VersionText.Text = string.IsNullOrWhiteSpace(version)
            ? "solarSim updated"
            : $"solarSim {version}";
        ReleasedText.Text = string.IsNullOrWhiteSpace(releasedLocal)
            ? ""
            : $"Released: {releasedLocal}";
        ReleasedText.Visibility = string.IsNullOrWhiteSpace(releasedLocal)
            ? Visibility.Collapsed
            : Visibility.Visible;
        NotesText.Text = string.IsNullOrWhiteSpace(notes)
            ? "This update is installed."
            : notes.Trim();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(true);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            Completed?.Invoke(true);
            e.Handled = true;
        }
    }
}
