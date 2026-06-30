using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class MapGeneratorTests
{
    private static MapGenerationSettings Settings(int seed = 2025) => new()
    {
        Width = 40,
        Height = 30,
        Seed = seed
    };

    [Fact]
    public void Generates_Exactly_Width_Times_Height_Tiles()
    {
        var map = new MapGenerator(Settings()).Generate();
        Assert.Equal(40 * 30, map.Count);
    }

    [Fact]
    public void Same_Seed_Produces_Identical_Maps()
    {
        var a = new MapGenerator(Settings(seed: 99)).Generate();
        var b = new MapGenerator(Settings(seed: 99)).Generate();

        var ta = a.Tiles.OrderBy(t => t.Coord.Q).ThenBy(t => t.Coord.R).ToList();
        var tb = b.Tiles.OrderBy(t => t.Coord.Q).ThenBy(t => t.Coord.R).ToList();

        Assert.Equal(ta.Count, tb.Count);
        for (int i = 0; i < ta.Count; i++)
        {
            Assert.Equal(ta[i].Coord, tb[i].Coord);
            Assert.Equal(ta[i].Terrain, tb[i].Terrain);
            Assert.Equal(ta[i].Elevation, tb[i].Elevation);
            Assert.Equal(ta[i].Deposit.Type, tb[i].Deposit.Type);
            Assert.Equal(ta[i].Deposit.Amount, tb[i].Deposit.Amount);
        }
    }

    [Fact]
    public void Different_Seeds_Produce_Different_Maps()
    {
        var a = new MapGenerator(Settings(seed: 1)).Generate();
        var b = new MapGenerator(Settings(seed: 2)).Generate();

        var byCoordB = b.Tiles.ToDictionary(t => t.Coord);
        int differences = a.Tiles.Count(t =>
            byCoordB.TryGetValue(t.Coord, out var other) && other.Terrain != t.Terrain);

        Assert.True(differences > 0, "Διαφορετικά seeds πρέπει να δίνουν διαφορετικό χάρτη.");
    }

    [Fact]
    public void Map_Has_Terrain_Variety_And_Some_Ice()
    {
        var map = new MapGenerator(Settings()).Generate();

        int distinctTerrains = map.Tiles.Select(t => t.Terrain).Distinct().Count();
        Assert.True(distinctTerrains >= 3, "Ο χάρτης πρέπει να έχει ποικιλία terrain.");

        bool hasIce = map.Tiles.Any(t => t.Deposit.Type == ResourceType.Ice);
        Assert.True(hasIce, "Πρέπει να υπάρχει πάγος (κρίσιμος για νερό/οξυγόνο).");

        double withResources = map.Tiles.Count(t => !t.Deposit.IsEmpty) / (double)map.Count;
        Assert.InRange(withResources, 0.05, 0.95);
    }

    [Fact]
    public void Top_Row_Is_A_Polar_Ice_Cap()
    {
        var map = new MapGenerator(Settings()).Generate();

        var topRow = map.Tiles.Where(t => OffsetCoord.FromHex(t.Coord).Row == 0);
        Assert.All(topRow, t => Assert.Equal(TerrainType.PolarIce, t.Terrain));
    }
}
