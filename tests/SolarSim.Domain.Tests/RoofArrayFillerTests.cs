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

    [Fact]
    public void Wraps_to_a_second_row_instead_of_one_long_line()
    {
        // 14 m × 4 m holds 10 portrait modules in a single row, or 2×5 landscape.
        var roofs = new RoofDocument();
        var roof = roofs.AddRoof();
        roof.SetbackMm = 0;
        roof.SetVertices(
        [
            new Point2Mm(0, 0),
            new Point2Mm(14000, 0),
            new Point2Mm(14000, 4000),
            new Point2Mm(0, 4000),
        ], closed: true);

        var packed = RoofArrayFiller.Pack(roofs, 1134, 2278, maxCount: 10, occupied: []);
        Assert.Equal(10, packed.Count);
        var rows = packed.Select(p => Math.Round(p.YMm / 10)).Distinct().Count();
        var cols = packed.Select(p => Math.Round(p.XMm / 10)).Distinct().Count();
        Assert.True(rows >= 2, $"expected a wrapped grid, got {rows}×{cols}");
        Assert.True(Math.Max(rows, cols) <= 5);
    }

    [Fact]
    public void Short_roof_still_fills_two_rows_when_one_row_cannot_hold_ten()
    {
        var roofs = new RoofDocument();
        var roof = roofs.AddRoof();
        roof.SetbackMm = 0;
        roof.SetVertices(
        [
            new Point2Mm(0, 0),
            new Point2Mm(7000, 0),
            new Point2Mm(7000, 6000),
            new Point2Mm(0, 6000),
        ], closed: true);

        var packed = RoofArrayFiller.Pack(roofs, 1134, 2278, maxCount: 10, occupied: []);
        Assert.Equal(10, packed.Count);
        var rows = packed.Select(p => Math.Round(p.YMm / 10)).Distinct().Count();
        Assert.True(rows >= 2);
    }
}
