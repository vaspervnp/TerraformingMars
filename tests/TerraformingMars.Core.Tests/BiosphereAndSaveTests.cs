using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Persistence;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class BiosphereTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    [Fact]
    public void Cyanobacteria_Raises_Atmospheric_Oxygen()
    {
        var map = Map();
        var colony = new Colony();
        var farm = new Building(BuildingCatalog.LoadDefault().Get("cyanobacteria_farm"), map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(farm);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, farm);

        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
        double before = world.Planet.Oxygen;
        for (int i = 0; i < 200; i++) world.Tick();

        Assert.True(world.Planet.Oxygen > before);
    }

    [Fact]
    public void Vegetation_Spreads_When_Warm_And_Wet()
    {
        var map = Map();
        var colony = new Colony();
        var farm = new Building(BuildingCatalog.LoadDefault().Get("gm_forest"), map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(farm);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, farm);

        var world = new World(map, colony, new ISimulationSystem[] { new BiosphereSystem() });
        world.Planet.Restore(10, 5, 1, 0.20, 0); // ζεστά & υγρά

        int vegBefore = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);
        for (int i = 0; i < 500; i++) world.Tick();
        int vegAfter = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

        Assert.True(vegAfter > vegBefore);
        Assert.True(world.Planet.Biomass > 0);
    }

    [Fact]
    public void Planet_Is_Terraformed_When_All_Metrics_Reach_Targets()
    {
        var map = Map();
        var world = new World(map, new Colony(), Array.Empty<ISimulationSystem>());
        Assert.False(world.IsTerraformed);

        world.Planet.Restore(
            PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0.5);

        Assert.True(world.IsTerraformed);
    }
}

public class PopulationTests
{
    [Fact]
    public void Population_Grows_With_Housing_And_Food()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 5 }).Generate();
        var world = ColonyFactory.CreateStartingWorld(map); // normal → BaseHousing 12, crew 4
        int start = world.Colony.Colonists.Count;

        for (int i = 0; i < 1000; i++) world.Tick();

        Assert.True(world.Colony.Colonists.Count > start);
        Assert.True(world.Colony.Colonists.Count <= world.Colony.Housing); // όριο στέγασης
    }
}

public class SaveLoadTests
{
    [Fact]
    public void Save_Then_Load_Restores_Core_State()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 5 }).Generate();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = ColonyFactory.CreateStartingWorld(map, catalog, sponsors.Get("normal"), enableEvents: true);

        world.Colony.Tech.Researched.Add("nuclear_fission");
        world.Planet.Restore(10, 20, 5, 0.2, 0.05); // ζεστά → ο πάγος λιώνει μέσω του PlanetSystem
        for (int i = 0; i < 50; i++) world.Update(0.25);
        var melted = map.Tiles.First(t => t.Terrain == TerrainType.Water); // tile που έλιωσε

        string json = SaveSystem.ToJson(world, sponsors.Get("normal"));
        var loaded = SaveSystem.Load(json, catalog, sponsors, out var sponsor);

        Assert.Equal("normal", sponsor.Id);
        Assert.Equal(world.Clock.TotalTicks, loaded.Clock.TotalTicks);
        Assert.Equal(world.Colony.Buildings.Count, loaded.Colony.Buildings.Count);
        Assert.Equal(world.Colony.Colonists.Count, loaded.Colony.Colonists.Count);
        Assert.Equal(world.Colony.Ledger.Get(ResourceKind.Credits), loaded.Colony.Ledger.Get(ResourceKind.Credits), 3);
        Assert.True(loaded.Colony.Tech.IsResearched("nuclear_fission"));
        Assert.Equal(world.Planet.Temperature, loaded.Planet.Temperature, 3);
        Assert.Equal(TerrainType.Water, loaded.Map.GetTile(melted.Coord)!.Terrain);
    }

    [Fact]
    public void Loaded_Game_Continues_Ticking_Without_Error()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 5 }).Generate();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = ColonyFactory.CreateStartingWorld(map, catalog, sponsors.Get("normal"), enableEvents: true);
        for (int i = 0; i < 100; i++) world.Update(0.25);

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);
        var exception = Record.Exception(() => { for (int i = 0; i < 100; i++) loaded.Update(0.25); });

        Assert.Null(exception);
    }
}
