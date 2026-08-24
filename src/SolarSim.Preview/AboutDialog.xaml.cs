using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SolarSim.Preview;

public partial class AboutDialog : UserControl, IAppModal
{
    public event Action<bool?>? Completed;

    public AboutDialog(string version)
    {
        InitializeComponent();
        VersionText.Text = $"Version {version}  ·  design aid";
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
