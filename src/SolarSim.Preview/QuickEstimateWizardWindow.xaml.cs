using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SolarSim.Application.Project;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Estimate;

namespace SolarSim.Preview;

public partial class QuickEstimateWizardWindow : UserControl, IAppModal
{
    private readonly SolarProject _project;
    private QuickEstimateInput? _input;
    private QuickSystemEstimateResult? _result;

    public event Action<bool?>? Completed;
    public bool Applied { get; private set; }

    public QuickEstimateWizardWindow(SolarProject project)
    {
        _project = project;
        InitializeComponent();
        Loaded += (_, _) => KwhBox.Focus();
    }

    private void Finish(bool applied)
    {
        Applied = applied;
        Completed?.Invoke(applied);
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish(false);

    private void Dialog_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish(false);
            e.Handled = true;
        }
    }

    private void Period_Changed(object sender, RoutedEventArgs e)
    {
        if (KwhLabel is null || PeriodYear is null)
            return;
        KwhLabel.Text = PeriodYear.IsChecked == true ? "kWh per year" : "kWh per month";
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadKwh(out var kwh, out var yearly, out var error))
        {
            ShowError(error);
            return;
        }

        ShowError(null);
        var panel = SolarPanelDefinition.CreateGeneric(550);
        _input = new QuickEstimateInput
        {
            UtilityId = GenericFlatTariff.UtilityId,
            UsageKind = yearly ? UsageInputKind.AnnualKwh : UsageInputKind.MonthlyKwh,
            MonthlyKwh = yearly ? null : kwh,
            AnnualKwh = yearly ? kwh : null,
            RoofMethod = RoofEstimateMethod.TraceLater,
            OffsetPercent = 100,
            BatteryGoal = BatteryGoal.None,
            PeakSunHours = 5.0,
            SystemDerate = 0.85,
            PanelDefinitionId = panel.Id,
            PanelWatts = panel.PmaxWatts,
            PanelWidthMm = panel.WidthMm,
            PanelHeightMm = panel.HeightMm,
            PanelLabel = panel.DisplayName,
        };

        _result = QuickSystemEstimateService.Compute(_input);
        ApplyRecommendedModule();

        _project.EnsureDefinition(ResolveWizardPanel());
        _project.InitialDesignTarget = _result.Target;
        QuickSystemEstimateService.ApplyToProject(_project.Site, _result.Target, _input);
        Finish(true);
    }

    private void ApplyRecommendedModule()
    {
        if (_result is null || _input is null)
            return;

        var usable = (_result.Roof.UsableLowFt2 + _result.Roof.UsableHighFt2) / 2.0;
        var watts = ModuleWattageAdvisor.Recommend(_result.RequiredDcKw, usable);
        if (Math.Abs(_input.PanelWatts - watts) <= 5)
            return;

        var def = SolarPanelDefinition.CreateGeneric(watts);
        _input.PanelDefinitionId = def.Id;
        _input.PanelWatts = def.PmaxWatts;
        _input.PanelWidthMm = def.WidthMm;
        _input.PanelHeightMm = def.HeightMm;
        _input.PanelLabel = def.DisplayName;
        _result = QuickSystemEstimateService.Compute(_input);
    }

    private SolarPanelDefinition ResolveWizardPanel()
    {
        var watts = (int)Math.Round(_input?.PanelWatts ?? _result?.Target.PanelWatts ?? 550);
        var def = SolarPanelDefinition.CreateGeneric(watts);
        if (_result is not null)
        {
            _result.Target.PreferredPanelDefinitionId = def.Id;
            _result.Target.PanelWatts = def.PmaxWatts;
            _result.Target.PanelLabel = def.DisplayName;
        }

        return def;
    }

    private bool TryReadKwh(out double kwh, out bool yearly, out string error)
    {
        kwh = 0;
        yearly = PeriodYear.IsChecked == true;
        error = "";

        if (!TryNumber(KwhBox.Text, out kwh) || kwh <= 0)
        {
            error = yearly
                ? "Enter kWh per year greater than zero."
                : "Enter kWh per month greater than zero.";
            return false;
        }

        return true;
    }

    private void ShowError(string? error)
    {
        ErrorText.Text = error ?? "";
        ErrorText.Visibility = string.IsNullOrEmpty(error) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool TryNumber(string? text, out double value)
    {
        var cleaned = (text ?? "").Replace(",", "").Trim();
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || double.TryParse(cleaned, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}
