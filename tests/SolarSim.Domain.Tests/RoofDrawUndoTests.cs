using SolarSim.Application.Commands;
using SolarSim.Application.Project;
using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class RoofDrawUndoTests
{
    [Fact]
    public void CtrlZ_removes_last_roof_vertex_only()
    {
        var project = new SolarProject();
        var roof = project.Roofs.EnsureActiveRoof();

        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(0, 0)));
        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(5000, 0)));
        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(5000, 4000)));
        Assert.Equal(3, roof.Vertices.Count);

        project.History.Undo();
        Assert.Equal(2, roof.Vertices.Count);
        Assert.Equal(5000, roof.Vertices[1].X);
        Assert.Equal(0, roof.Vertices[1].Y);

        project.History.Undo();
        Assert.Single(roof.Vertices);

        project.History.Redo();
        Assert.Equal(2, roof.Vertices.Count);
    }

    [Fact]
    public void Close_roof_is_undoable()
    {
        var project = new SolarProject();
        var roof = project.Roofs.EnsureActiveRoof();
        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(0, 0)));
        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(4000, 0)));
        project.History.Execute(new AddRoofVertexCommand(project, roof.Id, new Point2Mm(4000, 3000)));
        project.History.Execute(new CloseRoofCommand(project, roof.Id));

        Assert.True(roof.IsClosed);
        project.History.Undo();
        Assert.False(roof.IsClosed);
        Assert.Equal(3, roof.Vertices.Count);
    }
}
