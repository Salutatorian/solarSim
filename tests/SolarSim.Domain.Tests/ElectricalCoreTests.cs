using SolarSim.Application.Commands;
using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class SeriesCalculationTests
{
    private static (ElectricalGraph graph, Dictionary<Guid, SolarPanelDefinition> defs, List<SolarPanelInstance> panels)
        CreateIdenticalString(int count, SolarPanelDefinition? definition = null)
    {
        definition ??= SolarPanelDefinition.CreateBoviet270();
        var defs = new Dictionary<Guid, SolarPanelDefinition> { [definition.Id] = definition };
        var graph = new ElectricalGraph();
        var panels = new List<SolarPanelInstance>();

        for (var i = 0; i < count; i++)
        {
            var panel = new SolarPanelInstance(Guid.NewGuid(), definition.Id, i * 1200, 0);
            graph.AddPanel(panel);
            panels.Add(panel);
        }

        for (var i = 0; i < count - 1; i++)
        {
            var result = graph.TryConnect(
                panels[i].PositivePort.Id,
                panels[i + 1].NegativePort.Id,
                null,
                out _);
            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Message)));
        }

        return (graph, defs, panels);
    }

    [Fact]
    public void Single_panel_has_no_string_until_connected()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        graph.AddPanel(new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0));
        Assert.Empty(graph.Strings);
    }

    [Fact]
    public void Two_identical_panels_in_series_calculate_correctly()
    {
        var (graph, defs, _) = CreateIdenticalString(2);
        var calc = new ElectricalCalculationService();
        Assert.Single(graph.Strings);

        var result = calc.CalculateString(graph.Strings[0], graph.Panels, defs);

        Assert.Equal(2, result.PanelCount);
        Assert.Equal(540, result.TotalPmaxWatts, 3);
        Assert.Equal(62.4, result.VmpVolts, 3);
        Assert.Equal(76.2, result.VocVolts, 3);
        Assert.Equal(8.65, result.ImpAmps, 3);
        Assert.Equal(9.20, result.IscAmps, 3);
        Assert.False(result.IsMixedModuleString);
    }

    [Fact]
    public void Three_identical_panels_in_series_calculate_correctly()
    {
        var (graph, defs, _) = CreateIdenticalString(3);
        var calc = new ElectricalCalculationService();
        var result = calc.CalculateString(graph.Strings[0], graph.Panels, defs);

        Assert.Equal(3, result.PanelCount);
        Assert.Equal(810, result.TotalPmaxWatts, 3);
        Assert.Equal(93.6, result.VmpVolts, 3);
        Assert.Equal(114.3, result.VocVolts, 3);
        Assert.Equal(8.65, result.ImpAmps, 3);
        Assert.Equal(9.20, result.IscAmps, 3);
    }

    [Fact]
    public void Mixed_module_string_produces_warning_and_uses_min_current()
    {
        var boviet = SolarPanelDefinition.CreateBoviet270();
        var generic = SolarPanelDefinition.CreateGeneric400();
        var defs = new Dictionary<Guid, SolarPanelDefinition>
        {
            [boviet.Id] = boviet,
            [generic.Id] = generic,
        };

        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), boviet.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), generic.Id, 1200, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);

        var connect = graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _);
        Assert.True(connect.IsValid);

        var calc = new ElectricalCalculationService();
        var result = calc.CalculateString(graph.Strings[0], graph.Panels, defs);

        Assert.True(result.IsMixedModuleString);
        Assert.True(result.IsSimplified);
        Assert.Contains(result.Warnings, w => w.Code == "MIXED_MODULE_STRING");
        Assert.Equal(8.65, result.ImpAmps, 3); // min of 8.65 and 12.80
        Assert.Equal(670, result.TotalPmaxWatts, 3);
    }

    [Fact]
    public void Project_wattage_sums_all_placed_panels()
    {
        var (graph, defs, _) = CreateIdenticalString(3);
        var calc = new ElectricalCalculationService();
        var project = calc.CalculateProject(graph.Strings, graph.Panels, defs);
        Assert.Equal(810, project.TotalPmaxWatts, 3);
        Assert.Equal(1, project.StringCount);
        Assert.Equal(3, project.TotalPanels);
    }
}

public class ConnectionValidationTests
{
    [Fact]
    public void Positive_to_positive_is_rejected()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), def.Id, 1200, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);

        var result = graph.TryConnect(a.PositivePort.Id, b.PositivePort.Id, null, out var connection);
        Assert.False(result.IsValid);
        Assert.Null(connection);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_SERIES_CONNECTION");
        Assert.Empty(graph.Connections);
    }

    [Fact]
    public void Negative_to_negative_is_rejected()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), def.Id, 1200, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);

        var result = graph.TryConnect(a.NegativePort.Id, b.NegativePort.Id, null, out _);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "INVALID_SERIES_CONNECTION");
    }

    [Fact]
    public void Occupied_port_cannot_accept_second_wire()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), def.Id, 1200, 0);
        var c = new SolarPanelInstance(Guid.NewGuid(), def.Id, 2400, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);
        graph.AddPanel(c);

        Assert.True(graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);
        var second = graph.TryConnect(a.PositivePort.Id, c.NegativePort.Id, null, out _);
        Assert.False(second.IsValid);
        Assert.Contains(second.Errors, e => e.Code == "PORT_ALREADY_OCCUPIED");
    }

    [Fact]
    public void Duplicate_connection_is_rejected()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), def.Id, 1200, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);

        Assert.True(graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);
        var dup = graph.TryConnect(b.NegativePort.Id, a.PositivePort.Id, null, out _);
        Assert.False(dup.IsValid);
        Assert.Contains(dup.Errors, e => e.Code == "DUPLICATE_CONNECTION");
    }

    [Fact]
    public void Disconnect_removes_connection_and_rebuilds_strings()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        var b = new SolarPanelInstance(Guid.NewGuid(), def.Id, 1200, 0);
        graph.AddPanel(a);
        graph.AddPanel(b);
        graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out var connection);
        Assert.NotNull(connection);
        Assert.Single(graph.Strings);

        Assert.True(graph.Disconnect(connection!.Id));
        Assert.Empty(graph.Connections);
        Assert.Empty(graph.Strings);
        Assert.False(a.PositivePort.IsOccupied);
        Assert.False(b.NegativePort.IsOccupied);
    }

    [Fact]
    public void Same_panel_ports_cannot_connect()
    {
        var def = SolarPanelDefinition.CreateBoviet270();
        var graph = new ElectricalGraph();
        var a = new SolarPanelInstance(Guid.NewGuid(), def.Id, 0, 0);
        graph.AddPanel(a);

        var result = graph.TryConnect(a.PositivePort.Id, a.NegativePort.Id, null, out _);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Code == "SAME_COMPONENT");
    }
}

public class CommandAndSerializationTests
{
    [Fact]
    public void Undo_and_redo_connection()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);

        var connect = new ConnectPortsCommand(project, a.PositivePort.Id, b.NegativePort.Id);
        project.History.Execute(connect);
        Assert.Single(project.Graph.Connections);
        Assert.Single(project.Graph.Strings);

        project.History.Undo();
        Assert.Empty(project.Graph.Connections);
        Assert.Empty(project.Graph.Strings);

        project.History.Redo();
        Assert.Single(project.Graph.Connections);
        Assert.Single(project.Graph.Strings);

        var calc = project.GetCalculationSnapshot();
        Assert.Equal(540, calc.TotalPmaxWatts, 3);
        Assert.Equal(540, calc.Strings[0].TotalPmaxWatts, 3);
    }

    [Fact]
    public void Delete_panel_removes_connections_and_undo_restores()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.History.Execute(new ConnectPortsCommand(project, a.PositivePort.Id, b.NegativePort.Id));

        project.History.Execute(new DeletePanelCommand(project, b.Id));
        Assert.Single(project.Graph.Panels);
        Assert.Empty(project.Graph.Connections);

        project.History.Undo();
        Assert.Equal(2, project.Graph.Panels.Count);
        Assert.Single(project.Graph.Connections);
    }

    [Fact]
    public void Save_and_load_preserves_guids_and_topology()
    {
        var project = new SolarProject { Name = "TestArray" };
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 100, 200, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1300, 200, recordHistory: false);
        project.History.Execute(new ConnectPortsCommand(project, a.PositivePort.Id, b.NegativePort.Id));

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(project.ProjectId, loaded.ProjectId);
        Assert.Equal(2, loaded.Graph.Panels.Count);
        Assert.Single(loaded.Graph.Connections);
        Assert.True(loaded.Graph.Panels.ContainsKey(a.Id));
        Assert.True(loaded.Graph.Panels.ContainsKey(b.Id));
        Assert.Equal(a.PositivePort.Id, loaded.Graph.GetPanel(a.Id).PositivePort.Id);
        Assert.Equal(b.NegativePort.Id, loaded.Graph.GetPanel(b.Id).NegativePort.Id);

        var calc = loaded.GetCalculationSnapshot();
        Assert.Equal(540, calc.Strings[0].TotalPmaxWatts, 3);
    }

    [Fact]
    public void Duplicate_panel_gets_new_ids_and_no_connections()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, 1200, 0, recordHistory: false);
        project.History.Execute(new ConnectPortsCommand(project, a.PositivePort.Id, b.NegativePort.Id));

        var dupCmd = new DuplicatePanelCommand(project, a.Id);
        project.History.Execute(dupCmd);

        Assert.NotNull(dupCmd.DuplicateId);
        var dup = project.Graph.GetPanel(dupCmd.DuplicateId!.Value);
        Assert.NotEqual(a.Id, dup.Id);
        Assert.NotEqual(a.PositivePort.Id, dup.PositivePort.Id);
        Assert.False(dup.PositivePort.IsOccupied);
        Assert.False(dup.NegativePort.IsOccupied);
        Assert.Equal(3, project.Graph.Panels.Count);
        Assert.Single(project.Graph.Connections);
    }
}
