using SolarSim.Application.Project;
using SolarSim.Application.Serialization;
using SolarSim.Domain.Electrical;
using SolarSim.Domain.Equipment;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class Phase08RackingLayoutTests
{
    [Fact]
    public void Attachment_points_generated_for_single_row()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, def.WidthMm + 20, 0, recordHistory: false);

        var layout = project.ComputeRackingLayout();

        Assert.Equal(1, layout.RowCount);
        Assert.Equal(2, layout.RailCount);
        Assert.True(layout.AttachmentCount >= 4);
        Assert.Equal(4, layout.EndClampCount);
        Assert.Equal(2, layout.MidClampCount);

        var minX = 0.0;
        var maxX = def.WidthMm * 2 + 20;
        var minY = 0.0;
        var maxY = def.HeightMm;
        foreach (var p in layout.AttachmentPoints)
        {
            Assert.InRange(p.X, minX - project.Racking.RailOverhangMm - 1, maxX + project.Racking.RailOverhangMm + 1);
            Assert.InRange(p.Y, minY - 1, maxY + 1);
        }
    }

    [Fact]
    public void Rail_length_equals_array_width_plus_overhang()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        project.Racking.RailOverhangMm = 150;
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, def.WidthMm, 0, recordHistory: false);

        var layout = project.ComputeRackingLayout();
        var expectedOneRail = def.WidthMm * 2 + 2 * 150;
        Assert.Equal(2, layout.RailCount);
        Assert.Equal(expectedOneRail * 2, layout.TotalRailLengthMm, 3);
    }

    [Fact]
    public void Empty_array_returns_empty_layout()
    {
        var project = new SolarProject();
        var layout = project.ComputeRackingLayout();
        Assert.Equal(0, layout.RailCount);
        Assert.Empty(layout.AttachmentPoints);
    }

    [Fact]
    public void Racking_parameters_roundtrip_schema_8()
    {
        var project = new SolarProject();
        project.Racking.RafterSpacingMm = 610;
        project.Racking.RailOverhangMm = 200;
        project.Racking.AttachmentEdgeOffsetMm = 180;
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 100, 100, recordHistory: false);

        var json = SolarProjectSerializer.Serialize(project);
        var loaded = SolarProjectSerializer.Deserialize(json);

        Assert.Equal(SolarProject.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(610, loaded.Racking.RafterSpacingMm);
        Assert.Equal(200, loaded.Racking.RailOverhangMm);
        Assert.Equal(180, loaded.Racking.AttachmentEdgeOffsetMm);
    }

    [Fact]
    public void Legacy_schema_7_loads_with_default_racking()
    {
        var json = """
            {
              "schemaVersion": 7,
              "projectId": "11111111-1111-1111-1111-111111111111",
              "name": "Legacy",
              "definitions": [],
              "panels": [],
              "connections": [],
              "canvas": {},
              "equipment": []
            }
            """;

        var loaded = SolarProjectSerializer.Deserialize(json);
        Assert.Equal(7, loaded.SchemaVersion);
        Assert.Equal(RackingParameters.DefaultRafterSpacingMm, loaded.Racking.RafterSpacingMm);
        Assert.Equal(RackingParameters.DefaultRailOverhangMm, loaded.Racking.RailOverhangMm);
    }

    [Fact]
    public void Bom_includes_racking_line_items()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, def.WidthMm + 20, 0, recordHistory: false);

        var bom = project.BuildBomSchedule();
        Assert.Contains(bom.Items, i => i.Category == "Racking" && i.Description.Contains("Rail"));
        Assert.Contains(bom.Items, i => i.Category == "Racking" && i.Description.Contains("attachment"));
        Assert.Contains(bom.Items, i => i.Category == "Racking" && i.Description.Contains("End clamp"));
        Assert.Contains(bom.Items, i => i.Category == "Racking" && i.Description.Contains("Mid clamp"));
        Assert.Contains("Racking", bom.ToPlainText());
    }
}
