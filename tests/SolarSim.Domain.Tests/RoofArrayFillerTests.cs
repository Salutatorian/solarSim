using SolarSim.Domain.Roof;

namespace SolarSim.Domain.Tests;

public class RoofArrayFillerTests
{
    [Fact]
    public void Packs_requested_count_inside_a_simple_roof()
    {
        var roofs = new RoofDocument();
        var roof = roofs.AddRoof();
        roof.SetbackMm = 0;
        roof.SetVertices(
        [
            new Point2Mm(0, 0),
            new Point2Mm(12000, 0),
            new Point2Mm(12000, 8000),
            new Point2Mm(0, 8000),
        ], closed: true);

        var packed = RoofArrayFiller.Pack(roofs, 1134, 2278, maxCount: 5, occupied: []);
        Assert.Equal(5, packed.Count);
        Assert.All(packed, pose =>
            Assert.True(RoofGeometry.EvaluatePanelPlacement(roofs, pose.XMm, pose.YMm, 1134, 2278).IsValid
                        || RoofGeometry.EvaluatePanelPlacement(roofs, pose.XMm, pose.YMm, 2278, 1134).IsValid));
    }

    [Fact]
    public void Caps_to_what_the_roof_can_hold()
    {
        var roofs = new RoofDocument();
        var roof = roofs.AddRoof();
        roof.SetbackMm = 0;
        roof.SetVertices(
        [
            new Point2Mm(0, 0),
            new Point2Mm(2500, 0),
            new Point2Mm(2500, 2500),
            new Point2Mm(0, 2500),
        ], closed: true);

        var packed = RoofArrayFiller.Pack(roofs, 1134, 2278, maxCount: 40, occupied: []);
        Assert.True(packed.Count >= 1);
        Assert.True(packed.Count < 40);
    }
}
