using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;

namespace SolarSim.Domain.Tests;

public class Phase11BatteryTests
{
    [Fact]
    public void Battery_connects_to_battery_disconnect()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var disc = project.AddBatteryDisconnect(2000, 0);

        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var inPos = disc.Ports.First(p => p.Label == "IN+");
        Assert.True(project.Graph.TryConnect(batPos.Id, inPos.Id, null, out _).IsValid);

        var batNeg = battery.Ports.First(p => p.Label == "BAT-");
        var inNeg = disc.Ports.First(p => p.Label == "IN-");
        Assert.True(project.Graph.TryConnect(batNeg.Id, inNeg.Id, null, out _).IsValid);
    }

    [Fact]
    public void Battery_cannot_mix_with_ac_ports()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(0, 0);
        var ac = project.AddAcDisconnect(2000, 0);
        var batPos = battery.Ports.First(p => p.Label == "BAT+");
        var acL = ac.Ports.First(p => p.Label == "AC IN L");
        Assert.False(project.Graph.TryConnect(batPos.Id, acL.Id, null, out _).IsValid);
    }

    [Fact]
    public void Single_line_includes_storage()
    {
        var project = new SolarProject();
        project.AddBattery(0, 0);
        project.AddBatteryDisconnect(2000, 0);
        project.AddPvDisconnect(4000, 0);
        var text = project.BuildSingleLineSummary();
        Assert.Contains("Storage:", text);
        Assert.Contains("battery", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Batt. Disc.", text);
    }

    [Fact]
    public void Battery_equipment_roundtrips()
    {
        var project = new SolarProject();
        var battery = project.AddBattery(1200, 3400);
        var disc = project.AddBatteryDisconnect(2200, 3400);
        battery.SetRotation(90);

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(EquipmentKind.Battery, loaded.Graph.Equipment[battery.Id].Kind);
        Assert.Equal(EquipmentKind.BatteryDisconnect, loaded.Graph.Equipment[disc.Id].Kind);
        Assert.Equal(90, loaded.Graph.Equipment[battery.Id].RotationDegrees, 3);
        Assert.Contains(loaded.Graph.Equipment[battery.Id].Ports, p => p.Label == "BAT+");
        Assert.Contains("Battery", loaded.BuildBomSchedule().ToPlainText());
    }
}
