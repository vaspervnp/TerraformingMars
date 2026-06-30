using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class SponsorTests
{
    [Fact]
    public void Loads_Three_Sponsors()
    {
        var catalog = SponsorCatalog.LoadDefault();
        Assert.Equal(3, catalog.All.Count);
        Assert.True(catalog.TryGet("hard", out var hard));
        Assert.Equal("Private Crowdfunding", hard!.Name);
    }

    [Fact]
    public void Starting_Resources_Scale_With_Sponsor()
    {
        var easy = SponsorCatalog.LoadDefault().Get("easy");
        var hard = SponsorCatalog.LoadDefault().Get("hard");
        var map = new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 5 }).Generate();

        var easyWorld = ColonyFactory.CreateStartingWorld(map, sponsor: easy);
        var hardWorld = ColonyFactory.CreateStartingWorld(map, sponsor: hard);

        Assert.True(easyWorld.Colony.Ledger.Get(ResourceKind.Credits) >
                    hardWorld.Colony.Ledger.Get(ResourceKind.Credits));
    }
}

public class EventSystemTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static SponsorProfile TestSponsor() => new()
    {
        Id = "test",
        EventChancePerTick = 0.0, // καθόλου αυθόρμητα γεγονότα → ντετερμινιστικό
        DustStormSolarFactor = 0.2,
        DustStormMinTicks = 300, DustStormMaxTicks = 300,
        SolarFlareMinTicks = 200, SolarFlareMaxTicks = 200,
        RepairTicks = 100
    };

    private static Building Add(string id, Colony colony, Hex at)
    {
        var b = new Building(BuildingCatalog.LoadDefault().Get(id), at, startOperational: true);
        colony.AddBuilding(b);
        return b;
    }

    [Fact]
    public void Dust_Storm_Cuts_Solar_Output()
    {
        var map = Map();
        var colony = new Colony();
        Add("solar_panel", colony, map.Tiles.First().Coord);

        var events = new EventSystem(TestSponsor(), 1);
        var world = new World(map, colony, new ISimulationSystem[] { events, new ProductionSystem() });

        events.Trigger(world, EventType.DustStorm);
        world.Tick();

        Assert.True(world.SolarEfficiency < 1.0);
        double energyRate = colony.Ledger.RatePerTick(ResourceKind.Energy);
        Assert.True(energyRate > 0 && energyRate < 4.0, "Τα ηλιακά πρέπει να μειωθούν αλλά όχι να μηδενιστούν.");
    }

    [Fact]
    public void Life_Support_Failure_Disables_Building_Then_Repairs()
    {
        var map = Map();
        var colony = new Colony();
        var o2 = Add("o2_recycler", colony, map.Tiles.First().Coord);

        var events = new EventSystem(TestSponsor(), 1); // RepairTicks 100
        var world = new World(map, colony, new ISimulationSystem[] { events });

        events.Trigger(world, EventType.LifeSupportFailure);
        Assert.Equal(BuildingState.Disabled, o2.State);

        for (int i = 0; i < 100; i++) world.Tick();
        Assert.Equal(BuildingState.Operational, o2.State);
    }

    [Fact]
    public void Engineer_Repairs_Faster()
    {
        var map = Map();
        var colony = new Colony();
        var o2 = Add("o2_recycler", colony, map.Tiles.First().Coord);
        var engineer = new Colonist("E", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.Assign(engineer, o2);

        var events = new EventSystem(TestSponsor(), 1); // RepairTicks 100, με Engineer ×2
        var world = new World(map, colony, new ISimulationSystem[] { events });
        events.Trigger(world, EventType.LifeSupportFailure);

        for (int i = 0; i < 50; i++) world.Tick(); // 50×2 = 100 → επισκευασμένο
        Assert.Equal(BuildingState.Operational, o2.State);
    }

    [Fact]
    public void Solar_Flare_Sickens_Colonists_When_Unprotected()
    {
        var map = Map();
        var colony = new Colony();
        var colonist = new Colonist("X", Specialty.Geologist);
        colony.Colonists.Add(colonist);

        var events = new EventSystem(TestSponsor(), 1);
        var world = new World(map, colony, new ISimulationSystem[] { events });

        events.Trigger(world, EventType.SolarFlare);
        for (int i = 0; i < 50; i++) world.Tick();

        Assert.True(colonist.Health < 1.0);
    }

    [Fact]
    public void Magnetosphere_Protects_Colonists_From_Flare()
    {
        var map = Map();
        var colony = new Colony();
        Add("magnetosphere_station", colony, map.Tiles.First().Coord); // ShieldsAtmosphere
        var colonist = new Colonist("X", Specialty.Geologist);
        colony.Colonists.Add(colonist);

        var events = new EventSystem(TestSponsor(), 1);
        var world = new World(map, colony, new ISimulationSystem[] { events });

        events.Trigger(world, EventType.SolarFlare);
        for (int i = 0; i < 50; i++) world.Tick();

        Assert.Equal(1.0, colonist.Health, 6);
    }

    [Fact]
    public void Cave_Discovery_Grants_Radiation_Shelter()
    {
        var map = Map();
        var colony = new Colony();
        var events = new EventSystem(TestSponsor(), 1);
        var world = new World(map, colony, new ISimulationSystem[] { events });

        Assert.False(world.HasCaveShelter);
        events.Trigger(world, EventType.CaveDiscovery);
        Assert.True(world.HasCaveShelter);
    }
}
