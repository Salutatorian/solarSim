using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase04InverterMpptTests
{
    [Fact]
    public void Inverter_has_mppt_ports_for_each_channel()
    {
        var project = new SolarProject();
        var inv = project.AddStringInverter(0, 0, InverterDefinition.CreateGeneric5kW2Mppt());
        Assert.Equal(EquipmentKind.StringInverter, inv.Kind);
        Assert.NotNull(inv.InverterSpecs);
        Assert.Equal(2, inv.InverterSpecs!.MpptCount);
        Assert.Equal(6, inv.Ports.Count);
        Assert.Contains(inv.Ports, p => p.Label == "MPPT1+");
        Assert.Contains(inv.Ports, p => p.Label == "MPPT1-");
        Assert.Contains(inv.Ports, p => p.Label == "MPPT2+");
        Assert.Contains(inv.Ports, p => p.Label == "MPPT2-");
        Assert.Contains(inv.Ports, p => p.Label == "BAT+");
        Assert.Contains(inv.Ports, p => p.Label == "BAT-");
    }

    [Fact]
    public void Compatible_string_on_mppt_has_no_errors()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);

        var inv = project.AddStringInverter(4000, 0);
        var mppt1Plus = inv.Ports.First(p => p.Label == "MPPT1+");
        var mppt1Minus = inv.Ports.First(p => p.Label == "MPPT1-");

        // Free ends after a+→b−: b+ (string +) and a− (string −)
        Assert.True(project.Graph.TryConnect(b.PositivePort.Id, mppt1Plus.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(a.NegativePort.Id, mppt1Minus.Id, null, out _).IsValid);

        var reports = project.GetMpptReports();
        Assert.Single(reports);
        var ch1 = reports[0].Channels.First(c => c.ChannelIndex == 1);
        Assert.True(ch1.PositiveConnected && ch1.NegativeConnected);
        Assert.Equal(2, ch1.PanelIds.Count);
        Assert.Single(ch1.StringIds);
        Assert.Equal(76.2, ch1.VocVolts!.Value, 1);
        Assert.Equal(62.4, ch1.VmpVolts!.Value, 1);
        Assert.DoesNotContain(ch1.Issues, i => i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void Voc_over_max_flags_error()
    {
        var project = new SolarProject();
        // Tiny max DC so any Boviet string fails.
        var tiny = new InverterDefinition(
            Guid.NewGuid(), "Test", "TinyVoc",
            acRatedWatts: 1000,
            mpptCount: 1,
            minMpptVolts: 20,
            maxMpptVolts: 40,
            maxDcVolts: 50,
            maxCurrentPerMpptAmps: 20,
            maxDcPowerPerMpptWatts: 2000);

        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _);

        var inv = project.AddStringInverter(4000, 0, tiny);
        var plus = inv.Ports.First(p => p.Label == "MPPT1+");
        var minus = inv.Ports.First(p => p.Label == "MPPT1-");
        Assert.True(project.Graph.TryConnect(b.PositivePort.Id, plus.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(a.NegativePort.Id, minus.Id, null, out _).IsValid);

        var ch = project.GetMpptReports()[0].Channels[0];
        Assert.Contains(ch.Issues, i => i.Code == "MPPT_VOC_EXCEEDED");
    }

    [Fact]
    public void Combiner_home_run_maps_string_to_mppt()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _);

        var combiner = project.AddCombiner(3000, 0, stringInputs: 2);
        var s1Plus = combiner.Ports.First(p => p.Label == "S1+");
        var s1Minus = combiner.Ports.First(p => p.Label == "S1-");
        var outPlus = combiner.Ports.First(p => p.Label == "OUT+");
        var outMinus = combiner.Ports.First(p => p.Label == "OUT-");

        project.Graph.TryConnect(b.PositivePort.Id, s1Plus.Id, null, out _);
        project.Graph.TryConnect(a.NegativePort.Id, s1Minus.Id, null, out _);

        var inv = project.AddStringInverter(5000, 0);
        var mpptPlus = inv.Ports.First(p => p.Label == "MPPT1+");
        var mpptMinus = inv.Ports.First(p => p.Label == "MPPT1-");
        Assert.True(project.Graph.TryConnect(outPlus.Id, mpptPlus.Id, null, out _).IsValid);
        Assert.True(project.Graph.TryConnect(outMinus.Id, mpptMinus.Id, null, out _).IsValid);

        var ch = project.GetMpptReports()[0].Channels.First(c => c.ChannelIndex == 1);
        Assert.Equal(2, ch.PanelIds.Count);
        Assert.Single(ch.StringIds);
        Assert.Equal(540, ch.PmaxWatts!.Value, 0);
    }

    [Fact]
    public void Inverter_roundtrips_in_project_file()
    {
        var project = new SolarProject();
        var inv = project.AddStringInverter(1000, 2000, InverterDefinition.CreateGeneric7_6kW3Mppt());
        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Single(loaded.Graph.Equipment);
        var loadedInv = loaded.Graph.Equipment[inv.Id];
        Assert.Equal(EquipmentKind.StringInverter, loadedInv.Kind);
        Assert.NotNull(loadedInv.InverterSpecs);
        Assert.Equal(3, loadedInv.InverterSpecs!.MpptCount);
        Assert.Equal(8, loadedInv.Ports.Count);
        Assert.Equal(inv.Ports[0].Id, loadedInv.Ports[0].Id);
    }

    [Fact]
    public void Anenji_hybrid_has_ac_battery_and_mppt_ports()
    {
        var project = new SolarProject();
        var inv = project.AddStringInverter(0, 0, InverterDefinition.CreateAnenji12kW2Mppt());
        Assert.Equal(2, inv.InverterSpecs!.MpptCount);
        Assert.Contains(inv.Ports, p => p.Label == "MPPT1+");
        Assert.Contains(inv.Ports, p => p.Label == "MPPT2-");
        Assert.Contains(inv.Ports, p => p.Label == "AC IN L");
        Assert.Contains(inv.Ports, p => p.Label == "AC IN G");
        Assert.Contains(inv.Ports, p => p.Label == "AC OUT N");
        Assert.Contains(inv.Ports, p => p.Label == "AC OUT G");
        Assert.Contains(inv.Ports, p => p.Label == "BAT+");
        Assert.Contains(inv.Ports, p => p.Label == "BAT-");
        Assert.Equal(12, inv.Ports.Count);
    }

    [Fact]
    public void Anenji_4_2kw_has_single_pv_ac_and_battery()
    {
        var project = new SolarProject();
        var inv = project.AddStringInverter(0, 0, InverterDefinition.CreateAnenji4_2kW1Mppt());
        Assert.Equal(1, inv.InverterSpecs!.MpptCount);
        Assert.Contains(inv.Ports, p => p.Label == "MPPT1+");
        Assert.DoesNotContain(inv.Ports, p => p.Label == "MPPT2+");
        Assert.Contains(inv.Ports, p => p.Label == "AC IN L");
        Assert.Contains(inv.Ports, p => p.Label == "AC IN G");
        Assert.Contains(inv.Ports, p => p.Label == "BAT+");
        Assert.Equal(10, inv.Ports.Count);
    }

    [Fact]
    public void Anenji_6_5kw_has_two_pv_ac_and_battery()
    {
        var project = new SolarProject();
        var inv = project.AddStringInverter(0, 0, InverterDefinition.CreateAnenji6_5kW2Mppt());
        Assert.Equal(2, inv.InverterSpecs!.MpptCount);
        Assert.Equal(6500, inv.InverterSpecs.AcRatedWatts);
        Assert.Contains(inv.Ports, p => p.Label == "MPPT1+");
        Assert.Contains(inv.Ports, p => p.Label == "MPPT2-");
        Assert.Contains(inv.Ports, p => p.Label == "AC IN L");
        Assert.Contains(inv.Ports, p => p.Label == "AC OUT G");
        Assert.Contains(inv.Ports, p => p.Label == "BAT+");
        Assert.Equal(12, inv.Ports.Count);
    }
}
