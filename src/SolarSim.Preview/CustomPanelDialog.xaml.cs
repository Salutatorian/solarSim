using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SolarSim.Application.Equipment;
using SolarSim.Domain.Equipment;

namespace SolarSim.Preview;

public partial class CustomPanelDialog : UserControl, IAppModal
{
    public event Action<bool?>? Completed;
    public SolarPanelDefinition? CreatedDefinition { get; private set; }

    public CustomPanelDialog()
    {
        InitializeComponent();
    }

    private void Create_Click(object sender, RoutedEventArgs e)
    {
        var request = new CustomPanelRequest
        {
            Manufacturer = ManufacturerBox.Text.Trim(),
            Model = ModelBox.Text.Trim(),
            PmaxWatts = Parse(PmaxBox.Text),
            VmpVolts = Parse(VmpBox.Text),
            ImpAmps = Parse(ImpBox.Text),
            VocVolts = Parse(VocBox.Text),
            IscAmps = Parse(IscBox.Text),
            WidthMm = Parse(WidthBox.Text),
            HeightMm = Parse(HeightBox.Text),
        };

        var errors = CustomPanelFactory.Validate(request);
        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        CreatedDefinition = CustomPanelFactory.Create(request);
        Completed?.Invoke(true);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(false);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Completed?.Invoke(false);
            e.Handled = true;
        }
    }

    private static double Parse(string text) =>
        double.TryParse(text, out var value) ? value : 0;
}
