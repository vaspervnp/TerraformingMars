using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class BrownoutTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static Building Add(string id, Colony colony, Hex at)
    {
        var b = new Building(BuildingCatalog.LoadDefault().Get(id), at, startOperational: true);
        colony.AddBuilding(b);
        return b;
    }

    [Fact]
    public void Consumers_Are_Throttled_Without_Power()
    {
        var map = Map();
        var tiles = map.Tiles.ToList();
        var colony = new Colony();
        var o2 = Add("o2_recycler", colony, tiles[0].Coord);
        var engineer = new Colonist("E", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.Assign(engineer, o2);

        var world = new World(map, colony, new ISimulationSystem[] { new ProductionSystem() });
        world.Tick();

        Assert.True(world.PowerOutage);
        Assert.Equal(0, colony.Ledger.Get(ResourceKind.Oxygen), 3); // καμία παραγωγή χωρίς ρεύμα
    }

    [Fact]
    public void Power_Source_Prevents_Brownout()
    {
        var map = Map();
        var tiles = map.Tiles.ToList();
        var colony = new Colony();
        var o2 = Add("o2_recycler", colony, tiles[0].Coord);
        var engineer = new Colonist("E", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.Assign(engineer, o2);
        Add("solar_panel", colony, tiles[1].Coord); // πηγή ενέργειας

        var world = new World(map, colony, new ISimulationSystem[] { new ProductionSystem() });
        world.Tick();

        Assert.False(world.PowerOutage);
        Assert.True(colony.Ledger.Get(ResourceKind.Oxygen) > 0);
    }
}

public class BalanceTests
{
    [Fact]
    public void Temperature_Stabilizes_Near_Target_Instead_Of_Overshooting()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();
        var tiles = map.Tiles.ToList();
        var colony = new Colony();
        var catalog = BuildingCatalog.LoadDefault();

        for (int i = 0; i < 10; i++)
        {
            var ghg = new Building(catalog.Get("ghg_factory"), tiles[i].Coord, startOperational: true);
            colony.AddBuilding(ghg);
            var climatologist = new Colonist($"C{i}", Specialty.Climatologist);
            colony.Colonists.Add(climatologist);
            colony.Assign(climatologist, ghg);
        }

        var world = new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
        for (int i = 0; i < 5000; i++) world.Tick();

        Assert.True(world.Planet.Temperature > PlanetState.TargetTemperature);  // πέρασε τον στόχο
        Assert.True(world.Planet.Temperature < 26.0);                           // αλλά δεν εκτοξεύτηκε (soft cap 25)
    }
}

public class LoseConditionTests
{
    [Fact]
    public void Colony_Collapses_After_Prolonged_Life_Support_Failure()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 16, Height = 16, Seed = 3 }).Generate();
        var colony = new Colony { Crew = 2 };
        colony.Colonists.Add(new Colonist("A", Specialty.Geologist));
        colony.Colonists.Add(new Colonist("B", Specialty.Engineer));
        colony.Crew = colony.Colonists.Count; // καθόλου προμήθειες → το life support αποτυγχάνει

        var world = new World(map, colony, new ISimulationSystem[] { new LifeSupportSystem() });
        Assert.False(world.IsLost);

        for (int i = 0; i < 2000; i++) world.Tick();

        Assert.True(world.IsLost);
        Assert.Empty(colony.Colonists);
    }

    [Fact]
    public void Supplied_Colony_Does_Not_Collapse()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 16, Height = 16, Seed = 3 }).Generate();
        var colony = new Colony { Crew = 1 };
        colony.Colonists.Add(new Colonist("A", Specialty.Geologist));
        colony.Crew = colony.Colonists.Count;
        colony.Ledger.Set(ResourceKind.Oxygen, 100_000);
        colony.Ledger.Set(ResourceKind.Water, 100_000);
        colony.Ledger.Set(ResourceKind.Food, 100_000);

        var world = new World(map, colony, new ISimulationSystem[] { new LifeSupportSystem() });
        for (int i = 0; i < 2000; i++) world.Tick();

        Assert.False(world.IsLost);
        Assert.Single(colony.Colonists);
    }
}
