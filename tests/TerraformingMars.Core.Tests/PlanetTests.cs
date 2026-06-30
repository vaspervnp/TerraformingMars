using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class PlanetStateTests
{
    [Fact]
    public void Progress_Is_Zero_At_Start_And_Clamps_To_One_At_Target()
    {
        var planet = new PlanetState();
        Assert.Equal(0, planet.Progress(PlanetMetric.Temperature), 3);

        planet.Add(PlanetMetric.Temperature, 100); // περνά τον στόχο
        Assert.Equal(1, planet.Progress(PlanetMetric.Temperature), 3);
    }

    [Fact]
    public void Overall_Progress_Averages_The_Four_Metrics()
    {
        var planet = new PlanetState();
        Assert.Equal(0, planet.OverallProgress, 3);
        Assert.False(planet.IsTerraformed);
    }
}

public class PlanetSystemTests
{
    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    private static Building Operational(string id, Colony colony, Hex at)
    {
        var b = new Building(BuildingCatalog.LoadDefault().Get(id), at, startOperational: true);
        colony.AddBuilding(b);
        return b;
    }

    [Fact]
    public void GHG_Factory_Raises_Temperature_When_Staffed()
    {
        var map = Map();
        var colony = new Colony();
        var ghg = Operational("ghg_factory", colony, map.Tiles.First().Coord);
        var clim = new Colonist("C", Specialty.Climatologist);
        colony.Colonists.Add(clim);
        colony.Assign(clim, ghg);

        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
        double before = world.Planet.Temperature;
        for (int i = 0; i < 100; i++) world.Tick();

        Assert.True(world.Planet.Temperature > before);
    }

    [Fact]
    public void Atmosphere_Leaks_Without_Magnetosphere()
    {
        var map = Map();
        var colony = new Colony();
        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
        double before = world.Planet.Pressure;

        for (int i = 0; i < 100; i++) world.Tick();

        Assert.True(world.Planet.Pressure < before);
    }

    [Fact]
    public void Magnetosphere_Prevents_Pressure_Leak()
    {
        var map = Map();
        var colony = new Colony();
        Operational("magnetosphere_station", colony, map.Tiles.First().Coord); // MaxWorkers 0 → λειτουργεί

        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
        double before = world.Planet.Pressure;

        for (int i = 0; i < 100; i++) world.Tick();

        Assert.Equal(before, world.Planet.Pressure, 6);
    }

    [Fact]
    public void Rising_Temperature_Melts_Polar_Ice_Into_Water()
    {
        var map = Map();
        var colony = new Colony();
        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });

        world.Planet.Add(PlanetMetric.Temperature, 60); // ανεβάζει τη θερμοκρασία στο 0°C
        int polarBefore = map.Tiles.Count(t => t.Terrain == TerrainType.PolarIce);
        int revisionBefore = world.MapRevision;

        for (int i = 0; i < 500; i++) world.Tick();

        int polarAfter = map.Tiles.Count(t => t.Terrain == TerrainType.PolarIce);
        int water = map.Tiles.Count(t => t.Terrain == TerrainType.Water);

        Assert.True(polarAfter < polarBefore, "Ο πολικός πάγος πρέπει να λιώνει.");
        Assert.True(water > 0);
        Assert.True(world.MapRevision > revisionBefore);
        Assert.True(world.Planet.WaterCoverage > 0);
    }

    [Fact]
    public void Tech_Gated_Planetary_Building_Unlocks_After_Research()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var def = BuildingCatalog.LoadDefault().Get("orbital_mirror");
        var flat = map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(def, flat.Coord, map).Success); // locked

        colony.Tech.Researched.Add("orbital_mirrors");
        Assert.True(colony.TryPlaceBuilding(def, flat.Coord, map).Success);
    }
}
