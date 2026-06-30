using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Research;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class TechCatalogTests
{
    [Fact]
    public void Loads_Default_Tech_Tree()
    {
        var catalog = TechCatalog.LoadDefault();
        Assert.NotEmpty(catalog.All);
        Assert.True(catalog.TryGet("nuclear_fission", out var fission));
        Assert.Equal(2, fission!.Phase);
        Assert.Contains("fission_reactor", fission.Unlocks);
    }
}

public class TechTreeTests
{
    [Fact]
    public void Prerequisites_Gate_Availability()
    {
        var tree = new TechTree();
        var ghg = tree.Catalog.Get("greenhouse_gas_production"); // prereq: nuclear_fission

        Assert.False(tree.CanResearch(ghg));
        Assert.DoesNotContain(tree.Available, t => t.Id == "greenhouse_gas_production");

        tree.Researched.Add("nuclear_fission");
        Assert.True(tree.CanResearch(ghg));
        Assert.Contains(tree.Available, t => t.Id == "greenhouse_gas_production");
    }

    [Fact]
    public void Research_Completes_When_Cost_Is_Reached()
    {
        var tree = new TechTree();
        var metallurgy = tree.Catalog.Get("heavy_metallurgy");

        Assert.True(tree.StartResearch("heavy_metallurgy"));
        tree.AddProgress(metallurgy.Cost - 10);
        Assert.False(tree.IsResearched("heavy_metallurgy"));

        tree.AddProgress(20);
        Assert.True(tree.IsResearched("heavy_metallurgy"));
        Assert.Null(tree.CurrentTarget);
    }

    [Fact]
    public void Cannot_Start_Research_With_Unmet_Prerequisites()
    {
        var tree = new TechTree();
        Assert.False(tree.StartResearch("orbital_mirrors")); // απαιτεί ghg → fission
    }

    [Fact]
    public void Unlocked_Building_Ids_Follow_Researched_Tech()
    {
        var tree = new TechTree();
        Assert.DoesNotContain("fission_reactor", tree.UnlockedBuildingIds);
        tree.Researched.Add("nuclear_fission");
        Assert.Contains("fission_reactor", tree.UnlockedBuildingIds);
    }
}

public class TechGatingTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    [Fact]
    public void Tech_Gated_Building_Cannot_Be_Placed_Until_Researched()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var fission = BuildingCatalog.LoadDefault().Get("fission_reactor");
        var flat = map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(fission, flat.Coord, map).Success); // locked

        colony.Tech.Researched.Add("nuclear_fission");
        Assert.True(colony.TryPlaceBuilding(fission, flat.Coord, map).Success); // unlocked
    }

    [Fact]
    public void Research_Lab_Advances_Current_Target_Over_Time()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 30, Height = 24, Seed = 5 }).Generate();
        var world = ColonyFactory.CreateStartingWorld(map);

        Assert.True(world.Colony.Tech.StartResearch("heavy_metallurgy")); // cost 250, χωρίς prereq
        for (int i = 0; i < 400; i++) world.Tick();

        Assert.True(world.Colony.Tech.IsResearched("heavy_metallurgy"));
    }
}
