using SolarSim.Domain.Equipment;

namespace SolarSim.Application.Equipment;

public sealed class CustomPanelRequest
{
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public double PmaxWatts { get; set; }
    public double VmpVolts { get; set; }
    public double ImpAmps { get; set; }
    public double VocVolts { get; set; }
    public double IscAmps { get; set; }
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double PositiveLeadLengthMm { get; set; } = 1000;
    public double NegativeLeadLengthMm { get; set; } = 1000;
    public string ConnectorFamily { get; set; } = "MC4-compatible";
}

public static class CustomPanelFactory
{
    public static IReadOnlyList<string> Validate(CustomPanelRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Manufacturer))
            errors.Add("Manufacturer is required.");
        if (string.IsNullOrWhiteSpace(request.Model))
            errors.Add("Model is required.");
        if (request.PmaxWatts <= 0) errors.Add("Rated power must be greater than zero.");
        if (request.VmpVolts <= 0) errors.Add("Vmp must be greater than zero.");
        if (request.ImpAmps <= 0) errors.Add("Imp must be greater than zero.");
        if (request.VocVolts <= 0) errors.Add("Voc must be greater than zero.");
        if (request.IscAmps <= 0) errors.Add("Isc must be greater than zero.");
        if (request.WidthMm <= 0) errors.Add("Width must be greater than zero.");
        if (request.HeightMm <= 0) errors.Add("Height must be greater than zero.");
        if (request.PositiveLeadLengthMm < 0) errors.Add("Positive lead length cannot be negative.");
        if (request.NegativeLeadLengthMm < 0) errors.Add("Negative lead length cannot be negative.");
        if (request.VocVolts < request.VmpVolts)
            errors.Add("Voc should be greater than or equal to Vmp.");
        if (request.IscAmps < request.ImpAmps)
            errors.Add("Isc should be greater than or equal to Imp.");
        return errors;
    }

    public static SolarPanelDefinition Create(CustomPanelRequest request)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors));

        return new SolarPanelDefinition(
            Guid.NewGuid(),
            request.Manufacturer,
            request.Model,
            request.PmaxWatts,
            request.VmpVolts,
            request.ImpAmps,
            request.VocVolts,
            request.IscAmps,
            request.WidthMm,
            request.HeightMm,
            positiveLeadLengthMm: request.PositiveLeadLengthMm,
            negativeLeadLengthMm: request.NegativeLeadLengthMm,
            connectorFamily: request.ConnectorFamily,
            isCustom: true);
    }
}
