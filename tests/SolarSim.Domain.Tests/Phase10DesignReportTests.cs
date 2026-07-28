using SolarSim.Application.Project;
using SolarSim.Application.Reports;
using SolarSim.Domain.Equipment;

namespace SolarSim.Domain.Tests;

public class Phase10DesignReportTests
{
    [Fact]
    public void Report_includes_modules_strings_and_bom()
    {
        var project = new SolarProject();
        var def = SolarPanelDefinition.CreateBoviet270();
        var a = project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);
        var b = project.AddPanelFromDefinition(def.Id, def.WidthMm + 20, 0, recordHistory: false);
        Assert.True(project.Graph.TryConnect(a.PositivePort.Id, b.NegativePort.Id, null, out _).IsValid);

        var report = project.BuildDesignReport();

        Assert.Equal(2, report.PanelCount);
        Assert.Equal(540, report.TotalDcWatts);
        Assert.Equal(1, report.StringCount);
        Assert.Equal(2, report.Modules.Count);
        Assert.Contains("SINGLE-LINE", report.SingleLineText);
        Assert.Contains("BOM", report.BomText);
        Assert.All(report.Modules, m => Assert.NotEqual("—", m.StringName));
    }

    [Fact]
    public void Html_export_contains_layout_and_schedule()
    {
        var project = new SolarProject { Name = "DemoArray" };
        var def = SolarPanelDefinition.CreateBoviet270();
        project.AddPanelFromDefinition(def.Id, 100, 200, recordHistory: false);
        project.AddPanelFromDefinition(def.Id, 100 + def.WidthMm + 20, 200, recordHistory: false);

        var html = DesignReportHtmlExporter.ToHtml(project.BuildDesignReport());

        Assert.Contains("DemoArray", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Module schedule", html);
        Assert.Contains("Single-line", html);
        Assert.Contains("Save as PDF", html);
    }

    [Fact]
    public void WriteToFile_creates_html_on_disk()
    {
        var project = new SolarProject { Name = "FileTest" };
        var def = SolarPanelDefinition.CreateGeneric400();
        project.AddPanelFromDefinition(def.Id, 0, 0, recordHistory: false);

        var path = Path.Combine(Path.GetTempPath(), $"solarSim_report_{Guid.NewGuid():N}.html");
        try
        {
            var written = project.ExportDesignReportHtml(path);
            Assert.Equal(path, written);
            Assert.True(File.Exists(path));
            var text = File.ReadAllText(path);
            Assert.Contains("FileTest", text);
            Assert.Contains("Generic 400 W", text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
