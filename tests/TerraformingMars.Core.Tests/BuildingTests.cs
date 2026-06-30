using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class BuildingCatalogTests
{
    [Fact]
    public void Loads_Default_Buildings_From_Embedded_Json()
    {
        var catalog = BuildingCatalog.LoadDefault();
        Assert.NotEmpty(catalog.All);
        Assert.True(catalog.TryGet("solar_panel", out var solar));
        Assert.Equal("Solar Panel Array", solar!.Name);
    }

    [Fact]
    public void Buildables_Excludes_Non_Buildable_Capsule()
    {
        var catalog = BuildingCatalog.LoadDefault();
        Assert.Contains(catalog.Buildables, d => d.Id == "solar_panel");
        Assert.DoesNotContain(catalog.Buildables, d => d.Id == "landing_capsule");
    }

    [Fact]
    public void Parses_Enum_Keyed_Dictionaries_And_Enum_Lists()
    {
        var solar = BuildingCatalog.LoadDefault().Get("solar_panel");
        Assert.Equal(4.0, solar.Production[ResourceKind.Energy]);
        Assert.Contains(TerrainType.Flatland, solar.AllowedTerrain);

        var iceDrill = BuildingCatalog.LoadDefault().Get("ice_drill");
        Assert.Equal(ResourceType.Ice, iceDrill.RequiresDeposit);
        Assert.Equal(Specialty.Geologist, iceDrill.OptimalSpecialty);
    }
}

public class BuildingPlacementTests
{
    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    private static Colony RichColony()
    {
        var c = new Colony();
        c.Ledger.Set(ResourceKind.Materials, 1000);
        c.Ledger.Set(ResourceKind.Credits, 100_000);
        return c;
    }

    private static HexTile BuildableFlat(HexMap map) =>
        map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable);

    [Fact]
    public void Place_Solar_Deducts_Credits_Upfront_But_Materials_Are_Gradual()
    {
        var map = Map();
        var colony = RichColony();
        var def = BuildingCatalog.LoadDefault().Get("solar_panel");
        double materials0 = colony.Ledger.Get(ResourceKind.Materials);
        double credits0 = colony.Ledger.Get(ResourceKind.Credits);

        var result = colony.TryPlaceBuilding(def, BuildableFlat(map).Coord, map);

        Assert.True(result.Success);
        Assert.Single(colony.Buildings);
        Assert.Equal(BuildingState.UnderConstruction, result.Building!.State);
        Assert.Equal(credits0 - def.Cost[ResourceKind.Credits], colony.Ledger.Get(ResourceKind.Credits));
        Assert.Equal(materials0, colony.Ledger.Get(ResourceKind.Materials)); // υλικά μπαίνουν σταδιακά
    }

    [Fact]
    public void Cannot_Place_On_Mountain()
    {
        var map = Map();
        var colony = RichColony();
        var mountain = map.Tiles.First(t => t.Terrain == TerrainType.Mountain);

        var result = colony.TryPlaceBuilding(BuildingCatalog.LoadDefault().Get("solar_panel"), mountain.Coord, map);

        Assert.False(result.Success);
        Assert.Empty(colony.Buildings);
    }

    [Fact]
    public void IceDrill_Requires_Ice_Deposit()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var def = catalog.Get("ice_drill");

        var noIce = map.Tiles.First(t => t.IsBuildable && t.Deposit.Type != ResourceType.Ice);
        Assert.False(RichColony().TryPlaceBuilding(def, noIce.Coord, map).Success);

        var ice = map.Tiles.First(t => t.IsBuildable && t.Deposit.Type == ResourceType.Ice);
        Assert.True(RichColony().TryPlaceBuilding(def, ice.Coord, map).Success);
    }

    [Fact]
    public void Cannot_Place_On_Occupied_Hex()
    {
        var map = Map();
        var colony = RichColony();
        var catalog = BuildingCatalog.LoadDefault();
        var hex = BuildableFlat(map).Coord;

        Assert.True(colony.TryPlaceBuilding(catalog.Get("solar_panel"), hex, map).Success);
        Assert.False(colony.TryPlaceBuilding(catalog.Get("battery"), hex, map).Success);
    }

    [Fact]
    public void Cannot_Place_Without_Enough_Resources()
    {
        var map = Map();
        var colony = new Colony(); // καθόλου πόροι
        var result = colony.TryPlaceBuilding(BuildingCatalog.LoadDefault().Get("solar_panel"), BuildableFlat(map).Coord, map);
        Assert.False(result.Success);
    }
}

public class BuildingLifecycleTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static Colony RichColony()
    {
        var c = new Colony();
        c.Ledger.Set(ResourceKind.Materials, 1000);
        c.Ledger.Set(ResourceKind.Credits, 100_000);
        return c;
    }

    private static HexTile BuildableFlat(HexMap map) =>
        map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.IsBuildable);

    [Fact]
    public void Construction_Completes_After_BuildTime()
    {
        var map = Map();
        var colony = RichColony();
        var def = BuildingCatalog.LoadDefault().Get("solar_panel");
        var building = colony.TryPlaceBuilding(def, BuildableFlat(map).Coord, map).Building!;

        var world = new World(map, colony, new ISimulationSystem[] { new ConstructionSystem() });
        for (int i = 0; i < def.BuildTimeTicks; i++) world.Tick();

        Assert.Equal(BuildingState.Operational, building.State);
    }

    [Fact]
    public void Engineer_Speeds_Up_Construction()
    {
        var map = Map();
        var colony = RichColony();
        var def = BuildingCatalog.LoadDefault().Get("o2_recycler");
        var building = colony.TryPlaceBuilding(def, BuildableFlat(map).Coord, map).Building!;

        var engineer = new Colonist("Eng", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        Assert.True(colony.Assign(engineer, building));

        var world = new World(map, colony, new ISimulationSystem[] { new ConstructionSystem() });
        int ticks = 0;
        while (building.State != BuildingState.Operational && ticks < 1000) { world.Tick(); ticks++; }

        Assert.Equal(BuildingState.Operational, building.State);
        Assert.True(ticks < def.BuildTimeTicks, "Ο Engineer πρέπει να επιταχύνει την κατασκευή.");
    }

    [Fact]
    public void Battery_Adds_Storage_Capacity_When_Operational()
    {
        var map = Map();
        var colony = RichColony();
        var def = BuildingCatalog.LoadDefault().Get("battery");
        var building = colony.TryPlaceBuilding(def, BuildableFlat(map).Coord, map).Building!;

        var world = new World(map, colony, new ISimulationSystem[] { new ConstructionSystem() });
        for (int i = 0; i < def.BuildTimeTicks; i++) world.Tick();

        Assert.Equal(BuildingState.Operational, building.State);
        Assert.Equal(3000, colony.Ledger.Capacity(ResourceKind.Energy));
    }

    [Fact]
    public void Construction_Consumes_Materials_Gradually_And_Stalls_Without_Them()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        colony.Ledger.Set(ResourceKind.Materials, 10); // < solar cost (40)

        var def = BuildingCatalog.LoadDefault().Get("solar_panel");
        var building = colony.TryPlaceBuilding(def, BuildableFlat(map).Coord, map).Building!;
        var world = new World(map, colony, new ISimulationSystem[] { new ConstructionSystem() });

        for (int i = 0; i < def.BuildTimeTicks; i++) world.Tick();
        Assert.Equal(BuildingState.UnderConstruction, building.State); // 10 υλικά δεν φτάνουν
        Assert.True(building.Stalled);

        colony.Ledger.Set(ResourceKind.Materials, 100); // εφοδιασμός
        for (int i = 0; i < def.BuildTimeTicks; i++) world.Tick();
        Assert.Equal(BuildingState.Operational, building.State);
    }

    [Fact]
    public void Assign_And_Unassign_Updates_Workers_And_Idle_List()
    {
        var catalog = BuildingCatalog.LoadDefault();
        var colony = new Colony();
        var building = new Building(catalog.Get("o2_recycler"), new Hex(0, 0), startOperational: true);
        colony.AddBuilding(building);
        var engineer = new Colonist("E", Specialty.Engineer);
        colony.Colonists.Add(engineer);

        Assert.Contains(engineer, colony.IdleColonists);
        Assert.True(colony.Assign(engineer, building));
        Assert.Same(building, engineer.Assignment);
        Assert.DoesNotContain(engineer, colony.IdleColonists);
        Assert.Equal(1.5, building.WorkerEfficiency());

        Assert.True(colony.Unassign(engineer));
        Assert.Null(engineer.Assignment);
        Assert.Contains(engineer, colony.IdleColonists);
        Assert.Equal(0.0, building.WorkerEfficiency());
    }

    [Fact]
    public void WorkerEfficiency_Reflects_Staffing_And_Specialty()
    {
        var catalog = BuildingCatalog.LoadDefault();

        var solar = new Building(catalog.Get("solar_panel"), new Hex(0, 0), startOperational: true);
        Assert.Equal(1.0, solar.WorkerEfficiency()); // αυτόματο (MaxWorkers 0)

        var o2 = new Building(catalog.Get("o2_recycler"), new Hex(1, 0), startOperational: true);
        Assert.Equal(0.0, o2.WorkerEfficiency()); // χωρίς προσωπικό

        o2.Workers.Add(new Colonist("wrong", Specialty.Botanist));
        Assert.Equal(1.0, o2.WorkerEfficiency()); // στελεχωμένο, λάθος ειδικότητα

        o2.Workers.Clear();
        o2.Workers.Add(new Colonist("eng", Specialty.Engineer));
        Assert.Equal(1.5, o2.WorkerEfficiency()); // στελεχωμένο + σωστός ειδικός
    }
}

public class StartingColonyTests
{
    [Fact]
    public void Starting_World_Has_Capsule_Buildings_And_Crew()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 30, Height = 24, Seed = 5 }).Generate();
        var world = ColonyFactory.CreateStartingWorld(map);

        Assert.Contains(world.Colony.Buildings, b => b.Definition.Id == "landing_capsule");
        Assert.Contains(world.Colony.Buildings, b => b.Definition.Id == "solar_panel");
        Assert.Equal(4, world.Colony.Crew);
        Assert.True(world.Colony.Buildings.Count >= 6);
    }

    [Fact]
    public void Specialists_Are_Assigned_To_Their_Optimal_Buildings()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 30, Height = 24, Seed = 5 }).Generate();
        var world = ColonyFactory.CreateStartingWorld(map);

        var hydroponics = world.Colony.Buildings.First(b => b.Definition.Id == "hydroponics");
        Assert.Contains(hydroponics.Workers, w => w.Specialty == Specialty.Botanist);
        Assert.True(hydroponics.WorkerEfficiency() >= 1.5); // botanist bonus
    }

    [Fact]
    public void Starting_World_Sustains_Life_Support_Over_Time()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 30, Height = 24, Seed = 5 }).Generate();
        var world = ColonyFactory.CreateStartingWorld(map);

        for (int i = 0; i < 500; i++) world.Tick();

        Assert.False(world.Colony.LifeSupportFailing);
    }
}
