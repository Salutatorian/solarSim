using System.Windows;

namespace SolarSim.Preview;

public partial class WhatsNewDialog : Window
{
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

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
