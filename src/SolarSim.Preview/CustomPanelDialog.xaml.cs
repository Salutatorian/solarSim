using System.Windows;
using SolarSim.Application.Equipment;
using SolarSim.Domain.Equipment;

namespace SolarSim.Preview;

public partial class CustomPanelDialog : Window
{
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
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static double Parse(string text) =>
        double.TryParse(text, out var value) ? value : 0;
}
