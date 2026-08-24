namespace SolarSim.Domain.Estimate;

public static class ApplianceSeeder
{
    public static List<ApplianceLine> FromProfile(HouseholdProfile home)
    {
        var lines = new List<ApplianceLine>();
        var bedrooms = Math.Max(0, home.Bedrooms);
        var baths = Math.Max(0, home.Bathrooms);
        var kitchens = Math.Max(0, home.Kitchens);
        var living = Math.Max(0, home.LivingRooms);
        var occupantsRaw = Math.Max(0, home.Occupants);
        var garage = Math.Max(0, home.GarageCars);
        var inhabited = bedrooms + baths + kitchens + living + occupantsRaw > 0;
        var occupants = inhabited ? Math.Max(1, occupantsRaw) : 0;

        var bedroomAc = home.BedroomAcCount >= 0 ? home.BedroomAcCount : Math.Max(0, bedrooms - 1);
        var largeAc = home.LargeAcCount >= 0 ? home.LargeAcCount : (living > 0 ? 1 : 0);
        var bedroomAcWatts = Math.Max(200, home.BedroomAcBtu / 10.0);
        var largeAcWatts = Math.Max(400, home.LargeAcBtu / 10.0);

        Add(lines, "bedroom-lighting", "Bedroom lighting", "Bedrooms", bedrooms, 15, 4, 7, 1, 15);
        Add(lines, "bedroom-fan", "Ceiling fan", "Bedrooms", bedrooms, 75, 8, 7, 0.7, 120);
        if (home.AcUnits.Count > 0)
        {
            var n = 0;
            foreach (var ac in home.AcUnits)
            {
                n++;
                var spec = ResolveAc(ac);
                Add(lines, $"ac-{n}", spec.Name, "Air conditioners", spec.Quantity, spec.Watts, spec.HoursPerDay, 7, spec.Duty, spec.SurgeWatts);
            }
        }
        else
        {
            Add(lines, "bedroom-ac", "Bedroom inverter AC", "Air conditioners", bedroomAc, bedroomAcWatts, 8, 7, 0.65, bedroomAcWatts * 1.4);
            Add(lines, "living-ac", "Living inverter AC", "Air conditioners", largeAc, largeAcWatts, 10, 7, 0.65, largeAcWatts * 1.5);
        }
        Add(lines, "bedroom-chargers", "Chargers / electronics", "Bedrooms", inhabited ? occupants : 0, 15, 4, 7, 0.5, 20);

        Add(lines, "bath-lighting", "Bathroom lighting", "Bathrooms", baths, 20, 2, 7, 1, 20);
        Add(lines, "bath-exhaust", "Bathroom exhaust fan", "Bathrooms", baths, 40, 0.5, 7, 1, 80);

        if (home.WaterHeater is WaterHeaterKind.ElectricTank)
            Add(lines, "water-heater", "Electric tank water heater", "Water", 1, 4500, 3, 7, 0.35, 4500);
        else if (home.WaterHeater is WaterHeaterKind.TanklessElectric)
            Add(lines, "water-heater", "Tankless electric water heater", "Water", 1, 8000, 1.2, 7, 0.4, 8000);

        Add(lines, "fridge", "Refrigerator", "Kitchen", inhabited ? Math.Max(1, kitchens) : 0, 150, 24, 7, 0.4, 600);
        Add(lines, "freezer", "Freezer", "Kitchen", kitchens > 0 ? 1 : 0, 100, 24, 7, 0.4, 500);
        Add(lines, "microwave", "Microwave", "Kitchen", kitchens, 1200, 0.3, 7, 1, 1200);
        Add(lines, "rice-cooker", "Rice cooker", "Kitchen", kitchens, 700, 0.6, 7, 0.8, 700);
        Add(lines, "air-fryer", "Air fryer", "Kitchen", kitchens, 1500, 0.4, 5, 1, 1500);

        if (home.Cooking is CookingKind.Electric or CookingKind.Mixed)
        {
            var hours = home.Cooking is CookingKind.Mixed ? 0.6 : 1.2;
            Add(lines, "stove", "Electric stove / range", "Kitchen", kitchens, 2500, hours, 7, 0.5, 2500);
        }

        Add(lines, "washer", "Clothes washer", "Laundry", kitchens > 0 || bedrooms > 0 ? 1 : 0, 500, 0.7, 4, 0.8, 800);
        if (home.Dryer is DryerKind.Electric)
            Add(lines, "dryer", "Electric dryer", "Laundry", 1, 3000, 0.7, 4, 1, 3000);

        Add(lines, "living-lighting", "Living-room lighting", "Living", living, 40, 5, 7, 1, 40);
        Add(lines, "tv", "Television", "Living", living, 120, 4, 7, 1, 150);

        if (garage > 0)
        {
            Add(lines, "garage-lighting", "Garage lighting", "Garage", 1, 40, 1, 7, 1, 40);
            Add(lines, "garage-tools", "Workshop tools", "Garage", 1, 800, 0.3, 2, 0.5, 1600);
        }

        if (home.EvCharger)
            Add(lines, "ev-charger", "EV charger", "Garage", 1, 7000, 2, 5, 1, 7000);

        if (home.WaterPump)
            Add(lines, "water-pump", "Water pump", "Water", 1, 750, 1.5, 7, 0.5, 1500);

        if (home.PoolPump)
            Add(lines, "pool-pump", "Pool pump", "Outdoor", 1, 1200, 6, 7, 1, 1800);

        Add(lines, "dehumidifier", "Dehumidifier", "Living", 0, 300, 8, 7, 0.7, 400);
        Add(lines, "dishwasher", "Dishwasher", "Kitchen", 0, 1200, 1, 4, 0.8, 1500);
        Add(lines, "coffee", "Coffee maker", "Kitchen", kitchens, 900, 0.2, 7, 1, 900);
        Add(lines, "computer", "Desktop computer", "Living", 0, 200, 6, 7, 1, 250);
        Add(lines, "gaming-pc", "Gaming PC", "Living", 0, 400, 3, 5, 1, 500);
        Add(lines, "chest-freezer", "Chest freezer", "Kitchen", 0, 120, 24, 7, 0.4, 500);
        Add(lines, "water-dispenser", "Water dispenser", "Kitchen", 0, 100, 24, 7, 0.3, 400);
        Add(lines, "induction", "Induction cooker", "Kitchen", 0, 1800, 0.8, 7, 0.5, 1800);
        Add(lines, "oven", "Electric oven", "Kitchen", 0, 2400, 0.5, 3, 1, 2400);

        return lines.Where(l => l.Quantity > 0 || l.Id is "dehumidifier" or "dishwasher" or "computer" or "gaming-pc"
            or "chest-freezer" or "water-dispenser" or "induction" or "oven").ToList();
    }

    public static double DailyKwh(IEnumerable<ApplianceLine> lines) =>
        lines.Sum(l => l.DailyKwh);

    public static double EssentialDailyKwh(IEnumerable<ApplianceLine> lines) =>
        lines.Where(l => l.EssentialDuringOutage).Sum(l => l.DailyKwh);

    public static double PeakContinuousWatts(IEnumerable<ApplianceLine> lines) =>
        lines.Where(l => l.EssentialDuringOutage).Sum(l => l.RunningWatts);

    public static double PeakSurgeWatts(IEnumerable<ApplianceLine> lines) =>
        lines.Where(l => l.EssentialDuringOutage).Sum(l => l.PeakSurgeWatts);

    private static void Add(
        List<ApplianceLine> lines,
        string id,
        string name,
        string group,
        int quantity,
        double watts,
        double hours,
        double daysPerWeek,
        double duty,
        double surge,
        bool essential = false)
    {
        lines.Add(new ApplianceLine
        {
            Id = id,
            Name = name,
            Group = group,
            Quantity = Math.Max(0, quantity),
            RatedWatts = watts,
            HoursPerDay = hours,
            DaysPerWeek = daysPerWeek,
            DutyCycle = duty,
            SurgeWatts = surge,
            EssentialDuringOutage = essential,
        });
    }

    public static AcResolved ResolveAc(AcUnit ac)
    {
        var hours = ac.HoursPerDay > 0 ? ac.HoursPerDay : 8;
        var btu = ac.Btu > 0 ? ac.Btu : 12_000;
        if (ac.Kind == AcKind.WindowBox)
        {
            var watts = ac.CustomWatts ?? Math.Max(400, btu / 10.0);
            return new AcResolved(
                $"{btu / 1000.0:0.#}k BTU window / boxed AC",
                Math.Max(0, ac.Quantity),
                watts,
                hours,
                0.70,
                watts * 1.8);
        }

        var splitWatts = ac.CustomWatts ?? Math.Max(300, btu / 12.0);
        return new AcResolved(
            $"{btu / 1000.0:0.#}k BTU mini-split",
            Math.Max(0, ac.Quantity),
            splitWatts,
            hours,
            0.55,
            splitWatts * 1.3);
    }

    public readonly record struct AcResolved(
        string Name,
        int Quantity,
        double Watts,
        double HoursPerDay,
        double Duty,
        double SurgeWatts);
}
