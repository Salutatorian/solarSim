namespace SolarSim.Domain.Estimate;

public static class RoofCapacityEstimator
{
    public const double SpacingAllowance = 1.15;
    public const double DefaultPitchDegrees = 20;
    public const double UsableLowFraction = 0.55;
    public const double UsableHighFraction = 0.70;
    public const double CirculationLow = 1.15;
    public const double CirculationHigh = 1.25;

    public static RoofCapacityEstimate Estimate(QuickEstimateInput input)
    {
        var pitch = Math.Max(0, input.RoofPitchDegrees);

        return input.RoofMethod switch
        {
            RoofEstimateMethod.HouseDimensions when input.HouseLengthFt is double length && input.HouseWidthFt is double width && length > 0 && width > 0
                => FromFootprint(length * width, length * width, pitch, input, EstimateConfidence.Medium, "From entered house dimensions. Trace the roof to confirm."),
            RoofEstimateMethod.RoofArea when input.RoofAreaFt2 is double area && area > 0
                => FromSurface(area * 0.95, area * 1.05, input, EstimateConfidence.Medium, "From entered roof area. Setbacks and obstructions still need a trace."),
            _ => FromRooms(input, pitch),
        };
    }

    public static int PanelsForUsableAreaFt2(double usableFt2, double panelWidthMm, double panelHeightMm)
    {
        var effective = EffectivePanelAreaFt2(panelWidthMm, panelHeightMm);
        if (effective <= 0 || usableFt2 <= 0)
            return 0;
        return (int)Math.Floor(usableFt2 / effective);
    }

    public static int PanelsForTracedRoofSqM(double areaSqM, double panelWidthMm, double panelHeightMm, double usableFraction = 0.62)
    {
        var usableFt2 = areaSqM * 10.7639 * usableFraction;
        return PanelsForUsableAreaFt2(usableFt2, panelWidthMm, panelHeightMm);
    }

    public static double EffectivePanelAreaFt2(double widthMm, double heightMm)
    {
        var m2 = Math.Max(0, widthMm) / 1000.0 * Math.Max(0, heightMm) / 1000.0;
        return m2 * 10.7639 * SpacingAllowance;
    }

    private static RoofCapacityEstimate FromRooms(QuickEstimateInput input, double pitch)
    {
        var home = input.Home;
        var floorLow =
            home.Bedrooms * 100 + home.Bathrooms * 35 + home.Kitchens * 100
            + home.LivingRooms * 150 + 30 + GarageLow(home.GarageCars);
        var floorHigh =
            home.Bedrooms * 180 + home.Bathrooms * 80 + home.Kitchens * 200
            + home.LivingRooms * 350 + 80 + GarageHigh(home.GarageCars);

        floorLow *= CirculationLow;
        floorHigh *= CirculationHigh;

        var stories = StoryCount(home.HouseType);
        var footLow = floorLow / stories;
        var footHigh = floorHigh / stories;

        return FromFootprint(
            footLow,
            footHigh,
            pitch,
            input,
            EstimateConfidence.Low,
            "Estimated from room counts. Never treat this as a measured roof. Trace the roof to improve it.");
    }

    private static RoofCapacityEstimate FromFootprint(
        double footLow,
        double footHigh,
        double pitchDegrees,
        QuickEstimateInput input,
        EstimateConfidence confidence,
        string note)
    {
        var pitchRad = pitchDegrees * Math.PI / 180.0;
        var cos = Math.Max(0.3, Math.Cos(pitchRad));
        var surfaceLow = footLow / cos;
        var surfaceHigh = footHigh / cos;
        return FromSurface(surfaceLow, surfaceHigh, input, confidence, note, footLow, footHigh);
    }

    private static RoofCapacityEstimate FromSurface(
        double surfaceLow,
        double surfaceHigh,
        QuickEstimateInput input,
        EstimateConfidence confidence,
        string note,
        double footprintLow = 0,
        double footprintHigh = 0)
    {
        var usableLow = surfaceLow * UsableLowFraction;
        var usableHigh = surfaceHigh * UsableHighFraction;
        var low = PanelsForUsableAreaFt2(usableLow, input.PanelWidthMm, input.PanelHeightMm);
        var high = PanelsForUsableAreaFt2(usableHigh, input.PanelWidthMm, input.PanelHeightMm);
        if (high < low)
            (low, high) = (high, low);

        return new RoofCapacityEstimate
        {
            FootprintLowFt2 = footprintLow,
            FootprintHighFt2 = footprintHigh,
            SurfaceLowFt2 = surfaceLow,
            SurfaceHighFt2 = surfaceHigh,
            UsableLowFt2 = usableLow,
            UsableHighFt2 = usableHigh,
            PanelCapacityLow = low,
            PanelCapacityHigh = high,
            PanelCapacityMid = (low + high) / 2,
            Confidence = confidence,
            Note = note,
        };
    }

    private static int StoryCount(HouseType type) => type switch
    {
        HouseType.TwoStory => 2,
        HouseType.ThreeStory => 3,
        _ => 1,
    };

    private static double GarageLow(int cars) => cars switch
    {
        <= 0 => 0,
        1 => 200,
        _ => 400,
    };

    private static double GarageHigh(int cars) => cars switch
    {
        <= 0 => 0,
        1 => 300,
        _ => 600,
    };
}
