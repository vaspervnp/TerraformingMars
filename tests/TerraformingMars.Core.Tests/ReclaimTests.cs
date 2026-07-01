using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Persistence;
using TerraformingMars.Core.Research;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class ReclaimTests
{
    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static Colony RichColony()
    {
        var c = new Colony();
        c.Ledger.Set(ResourceKind.Credits, 100_000);
        return c;
    }

    [Fact]
    public void Reclaim_Tech_Is_Available_From_The_Start()
    {
        var tree = new TechTree();
        var reclaim = tree.Catalog.Get("reclaim");

        Assert.Empty(reclaim.Prerequisites);
        Assert.True(tree.CanResearch(reclaim));
        Assert.Contains(tree.Available, t => t.Id == "reclaim");
    }

    [Fact]
    public void ReclaimFraction_Starts_At_90pct_And_Decays_2pct_Per_Sol_To_A_20pct_Floor()
    {
        var b = new Building(Catalog.Get("solar_panel"), new Hex(0, 0)) { CreatedTick = 0 };

        Assert.Equal(0.90, Colony.ReclaimFraction(b, 0), 3);                                  // 0 sols
        Assert.Equal(0.80, Colony.ReclaimFraction(b, (long)(5 * GameClock.TicksPerSol)), 3);  // 5 sols
        Assert.Equal(0.20, Colony.ReclaimFraction(b, (long)(35 * GameClock.TicksPerSol)), 3); // floor reached
        Assert.Equal(0.20, Colony.ReclaimFraction(b, (long)(100 * GameClock.TicksPerSol)), 3);// stays at floor
    }

    [Fact]
    public void ReclaimFraction_Counts_Sols_Since_CreatedTick()
    {
        long created = (long)(5 * GameClock.TicksPerSol);
        var b = new Building(Catalog.Get("solar_panel"), new Hex(0, 0)) { CreatedTick = created };

        Assert.Equal(0.90, Colony.ReclaimFraction(b, created), 3);                                   // just built
        Assert.Equal(0.80, Colony.ReclaimFraction(b, created + (long)(5 * GameClock.TicksPerSol)), 3);
    }

    [Fact]
    public void ReclaimValue_Is_Fraction_Of_The_Credit_Cost()
    {
        var solar = new Building(Catalog.Get("solar_panel"), new Hex(0, 0)) { CreatedTick = 0 };
        // solar_panel credit cost = 2000
        Assert.Equal(2000 * 0.90, RichColony().ReclaimValue(solar, 0), 3);
        Assert.Equal(2000 * 0.20, RichColony().ReclaimValue(solar, (long)(50 * GameClock.TicksPerSol)), 3);
    }

    [Fact]
    public void Reclaim_Refunds_Credits_And_Removes_The_Building()
    {
        var colony = RichColony();
        var battery = new Building(Catalog.Get("battery"), new Hex(0, 0), startOperational: true) { CreatedTick = 0 };
        colony.AddBuilding(battery);
        double credits0 = colony.Ledger.Get(ResourceKind.Credits);

        double refund = colony.Reclaim(battery, 0); // battery credit cost 1500 → 90% = 1350

        Assert.Equal(1350, refund, 3);
        Assert.Equal(credits0 + 1350, colony.Ledger.Get(ResourceKind.Credits), 3);
        Assert.Empty(colony.Buildings);
    }

    [Fact]
    public void Reclaim_Removes_Storage_Capacity_Contributed_By_The_Building()
    {
        var colony = RichColony();
        var battery = new Building(Catalog.Get("battery"), new Hex(0, 0), startOperational: true) { CreatedTick = 0 };
        colony.AddBuilding(battery);
        Assert.Equal(3000, colony.Ledger.Capacity(ResourceKind.Energy));

        colony.Reclaim(battery, 0);

        Assert.Equal(0, colony.Ledger.Capacity(ResourceKind.Energy));
    }

    [Fact]
    public void Reclaim_Frees_Assigned_Workers()
    {
        var colony = RichColony();
        var o2 = new Building(Catalog.Get("o2_recycler"), new Hex(0, 0), startOperational: true) { CreatedTick = 0 };
        colony.AddBuilding(o2);
        var engineer = new Colonist("Eng", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.Assign(engineer, o2);
        Assert.Same(o2, engineer.Assignment);

        colony.Reclaim(o2, 0);

        Assert.Null(engineer.Assignment);
        Assert.Contains(engineer, colony.IdleColonists);
        Assert.Empty(colony.Buildings);
    }

    [Fact]
    public void Non_Buildable_Capsule_Cannot_Be_Reclaimed()
    {
        var colony = RichColony();
        var capsule = new Building(Catalog.Get("landing_capsule"), new Hex(0, 0), startOperational: true);
        colony.AddBuilding(capsule);

        Assert.False(Colony.CanReclaim(capsule));
        Assert.Equal(0.0, colony.Reclaim(capsule, 0));
        Assert.Single(colony.Buildings); // still there
    }

    [Fact]
    public void TryPlaceBuilding_Records_The_Placement_Tick()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();
        var colony = RichColony();
        var flat = map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable);

        var placed = colony.TryPlaceBuilding(Catalog.Get("solar_panel"), flat.Coord, map, 720).Building!;

        Assert.Equal(720, placed.CreatedTick);
    }

    [Fact]
    public void CreatedTick_Survives_Save_And_Load()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 30, Height = 24, Seed = 5 }).Generate();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = ColonyFactory.CreateStartingWorld(map, Catalog, sponsors.Get("normal"));
        var flat = map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable && !world.Colony.IsOccupied(t.Coord));

        var placed = world.Colony.TryPlaceBuilding(Catalog.Get("solar_panel"), flat.Coord, map, 500).Building!;
        Assert.Equal(500, placed.CreatedTick);

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), Catalog, sponsors, out _);
        var loadedPlaced = loaded.Colony.Buildings.First(b => b.Location == flat.Coord);

        Assert.Equal(500, loadedPlaced.CreatedTick);
    }
}
