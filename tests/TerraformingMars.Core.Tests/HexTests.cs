using TerraformingMars.Core.Grid;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class HexTests
{
    [Fact]
    public void Cube_Coordinate_Always_Sums_To_Zero()
    {
        var h = new Hex(3, -1);
        Assert.Equal(0, h.Q + h.R + h.S);
    }

    [Fact]
    public void Distance_Is_Symmetric_And_Correct()
    {
        var a = new Hex(0, 0);
        var b = new Hex(3, -1);

        Assert.Equal(3, a.DistanceTo(b));
        Assert.Equal(a.DistanceTo(b), b.DistanceTo(a));
    }

    [Fact]
    public void Each_Hex_Has_Six_Distinct_Neighbors_At_Distance_One()
    {
        var center = new Hex(2, 2);
        var neighbors = center.Neighbors().ToList();

        Assert.Equal(6, neighbors.Count);
        Assert.Equal(6, neighbors.Distinct().Count());
        Assert.All(neighbors, n => Assert.Equal(1, center.DistanceTo(n)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 3)]
    [InlineData(-4, 7)]
    [InlineData(10, 2)]
    public void Offset_RoundTrips_Through_Axial(int col, int row)
    {
        var offset = new OffsetCoord(col, row);
        var back = OffsetCoord.FromHex(offset.ToHex());

        Assert.Equal(offset.Col, back.Col);
        Assert.Equal(offset.Row, back.Row);
    }

    [Fact]
    public void Pixel_RoundTrips_Back_To_Same_Hex()
    {
        var layout = new HexLayout(size: 16, originX: 100, originY: 100);

        foreach (var h in new[] { new Hex(0, 0), new Hex(3, -2), new Hex(-5, 4), new Hex(7, 1) })
        {
            var (x, y) = layout.HexToPixel(h);
            var round = layout.PixelToHex(x, y);
            Assert.Equal(h, round);
        }
    }
}
