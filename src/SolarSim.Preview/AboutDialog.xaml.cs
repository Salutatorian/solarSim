using System.Windows;

namespace SolarSim.Preview;

public partial class AboutDialog : Window
{
    public AboutDialog(string version)
    {
        InitializeComponent();
        VersionText.Text = $"Version {version}  ·  design aid";
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
