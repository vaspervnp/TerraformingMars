using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Persistence;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Research;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class Phase2TransitionTests
{
    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    // Οδηγεί τον πλανήτη στους στόχους. Χωρίς PlanetSystem, οι τιμές του Restore παραμένουν
    // σταθερές (το WaterCoverage δεν ξαναϋπολογίζεται από tiles), οπότε το IsTerraformed κρατά.
    private static void RestoreToTargets(World w, double temp = PlanetState.TargetTemperature) =>
        w.Planet.Restore(temp, PlanetState.TargetPressure, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    [Fact]
    public void Phase2_Transition_Latches_Unlocks_Tech_And_Seeds_Population()
    {
        var world = new World(Map(), new Colony(), new ISimulationSystem[] { new Phase2System() });
        Assert.False(world.Phase2Active);

        RestoreToTargets(world);
        world.Tick();

        Assert.True(world.Phase2Active);
        Assert.True(world.Colony.Tech.Phase2Unlocked);
        Assert.True(world.Phase2CelebrationPending);
        Assert.True(world.Colony.Population >= World.Phase2StartingPopulation);
    }

    [Fact]
    public void Phase2_Migration_Grows_Population_Each_Tick()
    {
        var world = new World(Map(), new Colony(), new ISimulationSystem[] { new SocietySystem() });
        world.Colony.Ledger.Set(ResourceKind.Food, 10_000);   // πλεόνασμα ώστε να μεγαλώνει, όχι να συρρικνώνεται
        world.Colony.Ledger.Set(ResourceKind.Water, 10_000);
        world.Colony.Ledger.Set(ResourceKind.Energy, 10_000);
        RestoreToTargets(world);
        world.Tick(); // μετάβαση, Population = 1000 (η SocietySystem δεν έχει τρέξει ακόμα σε Φάση 2)
        double afterTransition = world.Colony.Population;

        for (int i = 0; i < 50; i++) world.Tick();

        Assert.True(world.Colony.Population > afterTransition);
    }

    [Fact]
    public void Phase2_Stays_Latched_When_Metric_Dips()
    {
        var world = new World(Map(), new Colony(), new ISimulationSystem[] { new Phase2System() });
        RestoreToTargets(world);
        world.Tick();
        Assert.True(world.Phase2Active);

        world.Planet.Add(PlanetMetric.Temperature, -100); // το κλίμα καταρρέει ξανά
        Assert.False(world.IsTerraformed);
        Assert.True(world.Phase2Active); // one-way latch
    }
}

public class Phase2TechGatingTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    [Fact]
    public void AtmosphereSinkArrays_Tech_Is_Gated_By_Phase2()
    {
        var tree = new TechTree();
        tree.Researched.Add("greenhouse_gas_production"); // ο μόνος prerequisite
        var sink = tree.Catalog.Get("atmosphere_sink_arrays");

        Assert.False(tree.CanResearch(sink));
        Assert.DoesNotContain(tree.Available, t => t.Id == "atmosphere_sink_arrays");
        Assert.False(tree.StartResearch("atmosphere_sink_arrays"));

        tree.UnlockPhase2();

        Assert.True(tree.CanResearch(sink));
        Assert.Contains(tree.Available, t => t.Id == "atmosphere_sink_arrays");
        Assert.True(tree.StartResearch("atmosphere_sink_arrays"));
    }

    [Fact]
    public void Existing_Techs_Are_Not_Affected_By_The_Gate()
    {
        var tree = new TechTree();
        // RequiresPhase2 default false ⇒ οι υπάρχουσες τεχνολογίες δεν αλλάζουν συμπεριφορά.
        Assert.True(tree.CanResearch(tree.Catalog.Get("nuclear_fission")));
    }

    [Fact]
    public void CryoCarbonCapturer_Placement_Gated_Until_Researched()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var capturer = BuildingCatalog.LoadDefault().Get("cryo_carbon_capturer");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(capturer, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("atmosphere_sink_arrays");
        Assert.True(colony.TryPlaceBuilding(capturer, tile.Coord, map).Success); // unlocked
    }
}

public class Phase2ClimateTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static World WorldWithCapturer(out Colony colony)
    {
        var map = Map();
        colony = new Colony();
        var capturer = new Building(BuildingCatalog.LoadDefault().Get("cryo_carbon_capturer"),
            map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(capturer);
        return new World(map, colony, new ISimulationSystem[] { new PlanetSystem() });
    }

    [Fact]
    public void CryoCarbonCapturer_Cools_Overshooting_Atmosphere()
    {
        var world = WorldWithCapturer(out _);
        world.Planet.Restore(20, 25, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        double t0 = world.Planet.Temperature, p0 = world.Planet.Pressure;

        for (int i = 0; i < 200; i++) world.Tick();

        Assert.True(world.Planet.Temperature < t0); // ο sink ρίχνει τη θερμοκρασία (μόνο αρνητικό delta)
        Assert.True(world.Planet.Pressure < p0);
    }

    [Fact]
    public void Sink_Stops_Near_Target_Deadband()
    {
        var world = WorldWithCapturer(out _);
        world.Planet.Restore(2, 11, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

        for (int i = 0; i < 300; i++) world.Tick();

        // Η θερμοκρασία (χωρίς ατμοσφαιρική διαρροή) σταματά κοντά στον στόχο, δεν καταρρέει.
        Assert.True(world.Planet.Temperature <= 2.0 && world.Planet.Temperature >= -0.5);
    }
}

public class Phase2RunawayTests
{
    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    [Fact]
    public void Runaway_Decays_Then_Recovers_Colonist_Health()
    {
        var world = new World(Map(), new Colony(), new ISimulationSystem[] { new Phase2System() });
        var colonist = new Colonist("A", Specialty.Engineer);
        world.Colony.Colonists.Add(colonist);
        ToTargets(world);
        world.Tick(); // → Φάση 2

        world.Planet.Restore(30, 40, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        for (int i = 0; i < 100; i++) world.Tick();
        Assert.True(world.RunawayActive);
        double decayed = colonist.Health;
        Assert.True(decayed < 1.0);

        world.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        for (int i = 0; i < 50; i++) world.Tick();
        Assert.False(world.RunawayActive);
        Assert.True(colonist.Health > decayed); // ανάκαμψη μόλις επιστρέψει στο sweet spot
    }

    [Fact]
    public void Runaway_Never_Kills_Colonists_Or_Collapses()
    {
        var world = new World(Map(), new Colony(), new ISimulationSystem[] { new Phase2System() });
        world.Colony.Colonists.Add(new Colonist("A", Specialty.Engineer));
        ToTargets(world);
        world.Tick();

        world.Planet.Restore(60, 60, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        for (int i = 0; i < 5000; i++) world.Tick();

        Assert.Single(world.Colony.Colonists);                 // κανείς δεν πέθανε
        Assert.False(world.Colony.Collapsed);                  // ποτέ game-over
        Assert.True(world.Colony.Colonists[0].Health >= 0.25); // health floor
    }

    [Fact]
    public void Biosphere_Stops_Spreading_When_Too_Hot()
    {
        var map = Map();
        var colony = new Colony();
        var forest = new Building(BuildingCatalog.LoadDefault().Get("gm_forest"),
            map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(forest);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, forest);

        var world = new World(map, colony, new ISimulationSystem[] { new BiosphereSystem() });
        world.Planet.Restore(50, 5, 1, 0.2, 0); // πολύ ζεστά (> 40 °C)
        int before = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

        for (int i = 0; i < 500; i++) world.Tick();

        Assert.Equal(before, map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation)); // κανένα άπλωμα
    }

    [Fact]
    public void Runaway_Withers_Vegetation_And_Lowers_Biomass()
    {
        var map = Map();
        var colony = new Colony();
        var forest = new Building(BuildingCatalog.LoadDefault().Get("gm_forest"),
            map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(forest);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, forest);

        var world = new World(map, colony, new ISimulationSystem[] { new BiosphereSystem(), new Phase2System() });

        // Στάδιο A: μεγάλωσε βλάστηση στη Φάση 1 (ζεστά+υγρά αλλά ΟΧΙ terraformed).
        world.Planet.Restore(3, 5, 1, 0.2, 0);
        for (int i = 0; i < 600; i++) world.Tick();
        Assert.False(world.Phase2Active);
        Assert.True(map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation) > 0);

        // Στάδιο B: μετάβαση στη Φάση 2, θερμοκρασία εντός band (χωρίς runaway).
        world.Planet.Restore(2, 12, 16, 0.35, world.Planet.Biomass);
        world.Tick();
        Assert.True(world.Phase2Active);
        int vegBefore = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);
        double biomassBefore = world.Planet.Biomass;

        // Στάδιο C: hot runaway → μαράζωμα βλάστησης & πτώση biomass.
        world.Planet.Restore(60, 12, 16, 0.35, world.Planet.Biomass);
        for (int i = 0; i < 200; i++) world.Tick();

        Assert.True(map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation) < vegBefore);
        Assert.True(world.Planet.Biomass < biomassBefore);
    }
}

public class Phase2SaveTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    [Fact]
    public void Save_Load_RoundTrips_Phase2_State()
    {
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var colony = new Colony { BaseHousing = 12 };
        colony.Tech.Researched.Add("greenhouse_gas_production");
        var world = new World(Map(), colony, new ISimulationSystem[] { new Phase2System() });
        world.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        for (int i = 0; i < 50; i++) world.Tick(); // μετάβαση + μετανάστευση

        string json = SaveSystem.ToJson(world, sponsors.Get("normal"));
        var loaded = SaveSystem.Load(json, catalog, sponsors, out _);

        Assert.True(loaded.Phase2Active);
        Assert.True(loaded.Colony.Tech.Phase2Unlocked);
        Assert.Equal(world.Colony.Population, loaded.Colony.Population, 3);
        Assert.True(loaded.Colony.Tech.CanResearch(loaded.Colony.Tech.Catalog.Get("atmosphere_sink_arrays")));
    }

    [Fact]
    public void Old_Save_Without_Phase2_Loads_As_PrePhase2()
    {
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        string json = @"{
            ""Version"": 2, ""Seed"": 5, ""Width"": 24, ""Height"": 24, ""Ticks"": 0,
            ""Speed"": ""Normal"", ""SponsorId"": ""normal"", ""Crew"": 0,
            ""Planet"": { ""Temperature"": -60, ""Pressure"": 0.6, ""Oxygen"": 0.1, ""Water"": 0, ""Biomass"": 0 },
            ""Resources"": {}, ""Capacities"": {},
            ""Tech"": { ""Researched"": [], ""Current"": null, ""Progress"": 0 },
            ""Buildings"": [], ""Colonists"": [], ""TileOverrides"": [], ""Events"": []
        }";

        var loaded = SaveSystem.Load(json, catalog, sponsors, out _);

        Assert.False(loaded.Phase2Active);
        Assert.Equal(0, loaded.Colony.Population);
        Assert.False(loaded.Colony.Tech.Phase2Unlocked);
    }
}

public class Phase2SocietyTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    private static World Phase2World(Colony colony, out HexMap map)
    {
        map = Map();
        var w = new World(map, colony, new ISimulationSystem[] { new SocietySystem() });
        colony.Ledger.Set(ResourceKind.Food, 100_000);
        colony.Ledger.Set(ResourceKind.Water, 100_000);
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        ToTargets(w);
        w.Tick(); // μετάβαση στη Φάση 2 (Population = 1000)
        return w;
    }

    [Fact]
    public void Population_Grows_Toward_Housing_Cap_Then_Stops()
    {
        var colony = new Colony();
        var world = Phase2World(colony, out _);
        Assert.Equal(World.Phase2StartingPopulation, colony.Population, 3);

        for (int i = 0; i < 5000; i++) world.Tick();

        // Χωρίς extra κτίρια, το cap είναι η βάση (3000)· ο πληθυσμός φτάνει εκεί και σταματά.
        Assert.Equal(Colony.AggregateHousingBase, colony.Population, 1);
        Assert.False(world.StagnationActive);
    }

    [Fact]
    public void Overcapacity_Triggers_Stagnation_Decline_And_Production_Penalty()
    {
        var colony = new Colony();
        var world = Phase2World(colony, out _);
        colony.Population = 5000; // πάνω από το base cap (3000)

        world.Tick();

        Assert.True(world.StagnationActive);
        Assert.True(world.ProductionEfficiency < 1.0);
        Assert.True(colony.Population < 5000); // συρρικνώνεται προς το βιώσιμο
    }

    [Fact]
    public void Resource_Shortfall_Triggers_Stagnation()
    {
        var colony = new Colony();
        var world = Phase2World(colony, out _);
        colony.Population = 2000;                       // κάτω από το cap...
        colony.Ledger.Set(ResourceKind.Food, 0);       // ...αλλά χωρίς τροφή

        world.Tick();

        Assert.True(world.StagnationActive);
        Assert.True(colony.Population < 2000);
    }

    [Fact]
    public void Reaching_10k_Fires_Urbanization_And_Unlocks_Arcology()
    {
        var map = Map();
        var colony = new Colony();
        // Στέγαση αρκετή για >10k: 2 domeless cities (6000 έκαστη) + base 3000 = 15000.
        var catalog = BuildingCatalog.LoadDefault();
        colony.AddBuilding(new Building(catalog.Get("domeless_city"), map.Tiles.ElementAt(0).Coord, startOperational: true));
        colony.AddBuilding(new Building(catalog.Get("domeless_city"), map.Tiles.ElementAt(1).Coord, startOperational: true));
        var world = new World(map, colony, new ISimulationSystem[] { new SocietySystem() });
        colony.Ledger.Set(ResourceKind.Food, 100_000);
        colony.Ledger.Set(ResourceKind.Water, 100_000);
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        ToTargets(world);
        world.Tick();                 // → Φάση 2
        colony.Population = 9999;
        world.Tick();                 // μεγαλώνει σε >10000 → latch

        Assert.True(world.UrbanizationReached);

        // Το arcology είναι πλέον τοποθετήσιμο.
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var arco = catalog.Get("high_density_arcology");
        var tile = map.Tiles.First(t => t.IsBuildable && !colony.IsOccupied(t.Coord));
        Assert.True(colony.TryPlaceBuilding(arco, tile.Coord, map).Success);
    }

    [Fact]
    public void Arcology_Placement_Gated_By_Population()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var arco = BuildingCatalog.LoadDefault().Get("high_density_arcology");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(arco, tile.Coord, map).Success); // peak 0 < 10000

        colony.PeakPopulation = 10_000;
        Assert.True(colony.TryPlaceBuilding(arco, tile.Coord, map).Success);
    }

    // Review finding: το arcology ΔΕΝ πρέπει να ξανακλειδώνει αν ο πληθυσμός πέσει κάτω από 10k (peak latch).
    [Fact]
    public void Arcology_Stays_Unlocked_After_Population_Dips_Below_Threshold()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var arco = BuildingCatalog.LoadDefault().Get("high_density_arcology");
        var tile = map.Tiles.First(t => t.IsBuildable);

        colony.PeakPopulation = 12_000; // έφτασε κάποτε τα 12k
        colony.Population = 8_000;       // ...αλλά τώρα έπεσε (κρίση/stagnation)

        Assert.True(colony.TryPlaceBuilding(arco, tile.Coord, map).Success); // παραμένει ξεκλείδωτο
    }

    [Fact]
    public void UrbanizationReached_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var colony = new Colony();
        colony.AddBuilding(new Building(catalog.Get("domeless_city"), map.Tiles.ElementAt(0).Coord, startOperational: true));
        colony.AddBuilding(new Building(catalog.Get("domeless_city"), map.Tiles.ElementAt(1).Coord, startOperational: true));
        var world = new World(map, colony, new ISimulationSystem[] { new SocietySystem() });
        colony.Ledger.Set(ResourceKind.Food, 100_000);
        colony.Ledger.Set(ResourceKind.Water, 100_000);
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        ToTargets(world);
        world.Tick();
        colony.Population = 10_500;
        world.Tick();
        Assert.True(world.UrbanizationReached);

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);
        Assert.True(loaded.UrbanizationReached);
        loaded.Colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var arco = catalog.Get("high_density_arcology");
        var tile = loaded.Map.Tiles.First(t => t.IsBuildable && !loaded.Colony.IsOccupied(t.Coord));
        Assert.True(loaded.Colony.TryPlaceBuilding(arco, tile.Coord, loaded.Map).Success);
    }
}

public class Phase2FactionTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    [Fact]
    public void Low_Approval_Strikes_Only_The_Right_Building_Category()
    {
        var colony = new Colony();
        var world = new World(Map(), colony, new ISimulationSystem[] { new FactionSystem() });
        ToTargets(world);
        world.Tick(); // μετάβαση στη Φάση 2
        colony.EcologistApproval = 0.05;
        colony.IndustrialistApproval = 0.9;
        world.Tick(); // η FactionSystem υπολογίζει απεργίες

        Assert.True(world.EcologistStrike);
        Assert.False(world.IndustrialStrike);

        var catalog = BuildingCatalog.LoadDefault();
        Assert.True(world.IsOnStrike(catalog.Get("gm_forest")));    // Biosphere → σταματά
        Assert.False(world.IsOnStrike(catalog.Get("iron_mine")));   // Industry → όχι
        Assert.False(world.IsOnStrike(catalog.Get("solar_panel"))); // Power → ποτέ
    }

    [Fact]
    public void Ecologist_Strike_Stops_Vegetation_Spread()
    {
        var map = Map();
        var colony = new Colony();
        var catalog = BuildingCatalog.LoadDefault();
        var forest = new Building(catalog.Get("gm_forest"), map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(forest);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, forest);
        for (int i = 0; i < 5; i++) // 5 ορυχεία κρατούν χαμηλά την έγκριση Οικολόγων (μόνιμη απεργία)
            colony.AddBuilding(new Building(catalog.Get("iron_mine"), map.Tiles.ElementAt(i + 1).Coord, startOperational: true));

        var world = new World(map, colony, new ISimulationSystem[] { new BiosphereSystem(), new FactionSystem() });
        world.Planet.Restore(10, 12, 16, 0.35, 0); // terraformed (→Φάση 2) & ζεστά/υγρά για βλάστηση
        world.Tick();
        colony.EcologistApproval = 0.05;
        for (int i = 0; i < 30; i++) world.Tick();   // η απεργία εδραιώνεται
        Assert.True(world.EcologistStrike);

        int before = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);
        for (int i = 0; i < 1000; i++) world.Tick();
        Assert.Equal(before, map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation)); // παγωμένη εξάπλωση
    }

    [Fact]
    public void Town_Halls_Keep_Approval_Above_Strike_Threshold()
    {
        var map = Map();
        var colony = new Colony();
        var catalog = BuildingCatalog.LoadDefault();
        for (int i = 0; i < 5; i++) // βαριά βιομηχανία → πιέζει τους Οικολόγους
            colony.AddBuilding(new Building(catalog.Get("iron_mine"), map.Tiles.ElementAt(i).Coord, startOperational: true));
        for (int i = 0; i < 2; i++) // ...αλλά η διακυβέρνηση τους κρατά ικανοποιημένους
            colony.AddBuilding(new Building(catalog.Get("district_town_hall"), map.Tiles.ElementAt(i + 5).Coord, startOperational: true));

        var world = new World(map, colony, new ISimulationSystem[] { new FactionSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 1000; i++) world.Tick();

        Assert.False(world.EcologistStrike);
        Assert.True(colony.EcologistApproval > 0.35);
    }

    [Fact]
    public void District_Town_Hall_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var hall = BuildingCatalog.LoadDefault().Get("district_town_hall");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(hall, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("socio_political_synthesis");
        Assert.True(colony.TryPlaceBuilding(hall, tile.Coord, map).Success);
    }

    [Fact]
    public void Faction_Approval_Round_Trips_Through_Save()
    {
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var colony = new Colony { IndustrialistApproval = 0.33, EcologistApproval = 0.77 };
        var world = new World(Map(), colony, System.Array.Empty<ISimulationSystem>());

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(0.33, loaded.Colony.IndustrialistApproval, 3);
        Assert.Equal(0.77, loaded.Colony.EcologistApproval, 3);
    }
}

public class Phase2PollutionTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    // regolith_printer = MaxWorkers 0 (auto, eff 1.0) & PollutionPerTick 0.02 → ρυπαίνει αξιόπιστα σε test.
    private static Building Polluter(HexMap map, int index) =>
        new(BuildingCatalog.LoadDefault().Get("regolith_printer"), map.Tiles.ElementAt(index).Coord, startOperational: true);

    [Fact]
    public void Heavy_Industry_Emits_Pollution()
    {
        var map = Map();
        var colony = new Colony();
        var mine = Polluter(map, 0);
        colony.AddBuilding(mine);
        var world = new World(map, colony, new ISimulationSystem[] { new PollutionSystem() });
        ToTargets(world);
        world.Tick(); // → Φάση 2
        for (int i = 0; i < 50; i++) world.Tick();

        Assert.True(map.GetTile(mine.Location)!.Pollution > 0);
        Assert.True(world.PollutionLevel > 0);
    }

    [Fact]
    public void Pollution_Does_Not_Accumulate_Before_Phase2()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(Polluter(map, 0));
        var world = new World(map, colony, new ISimulationSystem[] { new PollutionSystem() });
        for (int i = 0; i < 100; i++) world.Tick(); // ΟΧΙ terraformed → όχι Φάση 2

        Assert.Equal(0, world.PollutionLevel);
        Assert.Equal(0, map.Tiles.Sum(t => t.Pollution), 6);
    }

    [Fact]
    public void High_Pollution_Withers_Adjacent_Vegetation()
    {
        var map = Map();
        var mineTile = map.Tiles.First(t => t.IsBuildable);
        HexTile? veg = null;
        for (int s = 0; s < 6; s++)
            if (map.GetTile(mineTile.Coord.Neighbor(s)) is { } n) { n.Terrain = TerrainType.Vegetation; veg = n; break; }
        Assert.NotNull(veg);

        var colony = new Colony();
        colony.AddBuilding(new Building(BuildingCatalog.LoadDefault().Get("regolith_printer"), mineTile.Coord, startOperational: true));
        var world = new World(map, colony, new ISimulationSystem[] { new PollutionSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 300; i++) world.Tick(); // η ρύπανση ξεπερνά το κατώφλι

        Assert.NotEqual(TerrainType.Vegetation, veg!.Terrain); // μαράθηκε
    }

    [Fact]
    public void Scrubber_Reduces_Nearby_Pollution()
    {
        var map = Map();
        var mineTile = map.Tiles.First(t => t.IsBuildable);
        var colony = new Colony();
        colony.AddBuilding(new Building(BuildingCatalog.LoadDefault().Get("regolith_printer"), mineTile.Coord, startOperational: true));
        var world = new World(map, colony, new ISimulationSystem[] { new PollutionSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 300; i++) world.Tick();
        double before = map.GetTile(mineTile.Coord)!.Pollution;
        Assert.True(before > 0);

        // Scrubber σε γειτονικό hex.
        var neighbor = mineTile.Coord.Neighbor(0);
        colony.AddBuilding(new Building(BuildingCatalog.LoadDefault().Get("atmospheric_scrubber"), neighbor, startOperational: true));
        for (int i = 0; i < 300; i++) world.Tick();

        Assert.True(map.GetTile(mineTile.Coord)!.Pollution < before);
    }

    [Fact]
    public void Pollution_Lowers_Ecologist_Approval()
    {
        var map = Map();
        var colony = new Colony();
        for (int i = 0; i < 3; i++) colony.AddBuilding(Polluter(map, i));
        var world = new World(map, colony, new ISimulationSystem[] { new PollutionSystem(), new FactionSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 1000; i++) world.Tick();

        Assert.True(world.PollutionLevel > 0);
        Assert.True(colony.EcologistApproval < 0.5); // ρύπανση + βιομηχανία → οι Οικολόγοι δυσαρεστούνται
    }

    [Fact]
    public void Pollution_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var tile = map.Tiles.First(t => t.IsBuildable);
        tile.Pollution = 3.0;
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(3.0, loaded.Map.GetTile(tile.Coord)!.Pollution, 3);
    }

    [Fact]
    public void Atmospheric_Scrubber_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var scrubber = BuildingCatalog.LoadDefault().Get("atmospheric_scrubber");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(scrubber, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("atmospheric_scrubbing");
        Assert.True(colony.TryPlaceBuilding(scrubber, tile.Coord, map).Success);
    }
}

public class Phase2BEconomyTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    private static Building Op(HexMap map, string id, int index) =>
        new(BuildingCatalog.LoadDefault().Get(id), map.Tiles.ElementAt(index).Coord, startOperational: true);

    [Fact]
    public void Industrial_Shift_Latches_At_50k()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(Op(map, "high_density_arcology", 0)); // +30000 cap
        colony.AddBuilding(Op(map, "high_density_arcology", 1)); // +30000 cap → cap ~63000
        var world = new World(map, colony, new ISimulationSystem[] { new SocietySystem() });
        colony.Ledger.Set(ResourceKind.Food, 1_000_000);
        colony.Ledger.Set(ResourceKind.Water, 1_000_000);
        colony.Ledger.Set(ResourceKind.Energy, 1_000_000);
        ToTargets(world);
        world.Tick();               // → Φάση 2
        colony.Population = 49_999;
        world.Tick();               // μεγαλώνει σε >50000 → latch

        Assert.True(world.IndustrialShiftReached);
        Assert.True(colony.PeakPopulation >= World.IndustrialShiftThreshold);
    }

    [Fact]
    public void Stock_Exchange_Gated_By_50k_Population()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var exchange = BuildingCatalog.LoadDefault().Get("interplanetary_stock_exchange");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(exchange, tile.Coord, map).Success); // peak pop 0

        colony.PeakPopulation = 50_000;
        Assert.True(colony.TryPlaceBuilding(exchange, tile.Coord, map).Success);
    }

    [Fact]
    public void Stock_Exchange_Sells_Surplus_Silicon_And_Materials()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(Op(map, "interplanetary_stock_exchange", 0));
        colony.Ledger.Set(ResourceKind.Silicon, 1000);
        colony.Ledger.Set(ResourceKind.Materials, 1000); // πάνω από το reserve (300)
        colony.Ledger.Set(ResourceKind.Credits, 0);
        var world = new World(map, colony, new ISimulationSystem[] { new MarketSystem() });

        for (int i = 0; i < 10; i++) world.Tick();

        Assert.True(colony.Ledger.Get(ResourceKind.Credits) > 0);
        Assert.True(colony.Ledger.Get(ResourceKind.Silicon) < 1000);
        Assert.True(colony.Ledger.Get(ResourceKind.Materials) < 1000);
    }

    [Fact]
    public void Stock_Exchange_Keeps_A_Materials_Reserve()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(Op(map, "interplanetary_stock_exchange", 0));
        colony.Ledger.Set(ResourceKind.Materials, 200); // κάτω από το reserve → δεν πωλείται
        var world = new World(map, colony, new ISimulationSystem[] { new MarketSystem() });

        for (int i = 0; i < 20; i++) world.Tick();

        Assert.Equal(200, colony.Ledger.Get(ResourceKind.Materials), 3);
    }

    [Fact]
    public void Quantum_Plant_Sells_Silicon_At_Ten_Times_Raw()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(Op(map, "quantum_processor_plant", 0));
        colony.Ledger.Set(ResourceKind.Silicon, 1000);
        colony.Ledger.Set(ResourceKind.Credits, 0);
        var world = new World(map, colony, new ISimulationSystem[] { new MarketSystem() });

        world.Tick();

        // 3 silicon × 350 = 1050 (vs raw 3 × 35 = 105).
        Assert.Equal(3 * 350, colony.Ledger.Get(ResourceKind.Credits), 3);
        Assert.Equal(997, colony.Ledger.Get(ResourceKind.Silicon), 3);
    }

    [Fact]
    public void Quantum_Plant_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var plant = BuildingCatalog.LoadDefault().Get("quantum_processor_plant");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(plant, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("quantum_processing");
        Assert.True(colony.TryPlaceBuilding(plant, tile.Coord, map).Success);
    }

    [Fact]
    public void IndustrialShiftReached_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.IndustrialShiftReached = true;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.True(loaded.IndustrialShiftReached);
    }
}

public class Phase2BSeismicTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    private static Building StaffedDrill(Colony colony, HexMap map, Hex at)
    {
        var drill = new Building(BuildingCatalog.LoadDefault().Get("deep_core_drill"), at, startOperational: true);
        colony.AddBuilding(drill);
        var geo = new Colonist("G", Specialty.Geologist);
        colony.Colonists.Add(geo);
        colony.Assign(geo, drill);
        return drill;
    }

    [Fact]
    public void Deep_Core_Drilling_Builds_Seismic_Stress()
    {
        var map = Map();
        var colony = new Colony();
        StaffedDrill(colony, map, map.Tiles.First(t => t.IsBuildable).Coord);
        var world = new World(map, colony, new ISimulationSystem[] { new SeismicSystem() });
        ToTargets(world);
        world.Tick(); // → Φάση 2
        for (int i = 0; i < 20; i++) world.Tick();

        Assert.True(world.SeismicStress > 0);
        Assert.True(world.SeismicLevel > 0);
    }

    [Fact]
    public void Seismic_Stress_Does_Not_Build_Before_Phase2()
    {
        var map = Map();
        var colony = new Colony();
        StaffedDrill(colony, map, map.Tiles.First(t => t.IsBuildable).Coord);
        var world = new World(map, colony, new ISimulationSystem[] { new SeismicSystem() });
        for (int i = 0; i < 100; i++) world.Tick(); // ΟΧΙ terraformed

        Assert.Equal(0, world.SeismicStress, 6);
    }

    [Fact]
    public void Marsquake_Cracks_Nearby_Buildings()
    {
        var map = Map();
        var drillTile = map.Tiles.First(t => t.IsBuildable && map.GetTile(t.Coord.Neighbor(0)) is { IsBuildable: true });
        var neighbor = drillTile.Coord.Neighbor(0);
        var colony = new Colony();
        StaffedDrill(colony, map, drillTile.Coord);
        // Μη-κρίσιμο γειτονικό κτίριο (Industry) — η κρίσιμη υποδομή (Power/LifeSupport) εξαιρείται.
        var factory = new Building(BuildingCatalog.LoadDefault().Get("regolith_printer"), neighbor, startOperational: true);
        colony.AddBuilding(factory);
        var world = new World(map, colony, new ISimulationSystem[] { new SeismicSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 400; i++) world.Tick(); // η πίεση ξεπερνά το κατώφλι → marsquake

        Assert.Equal(BuildingState.Disabled, factory.State);      // ράγισε το γειτονικό
        Assert.True(factory.RepairTicksRemaining > 0);
        Assert.Contains(world.EventNotifications, n => n.Contains("Marsquake"));
    }

    [Fact]
    public void Striking_Drill_Does_Not_Build_Seismic_Stress()
    {
        var map = Map();
        var colony = new Colony();
        StaffedDrill(colony, map, map.Tiles.First(t => t.IsBuildable).Coord);
        var world = new World(map, colony, new ISimulationSystem[] { new SeismicSystem() });
        ToTargets(world);
        world.Tick();                    // → Φάση 2
        world.IndustrialStrike = true;   // απεργία Βιομηχανικών (το drill είναι Industry)
        for (int i = 0; i < 100; i++) world.Tick();

        Assert.Equal(0, world.SeismicStress, 6); // απεργός drill → καμία σεισμική συσσώρευση
    }

    [Fact]
    public void Marsquake_Spares_Power_And_Life_Support()
    {
        var map = Map();
        var drillTile = map.Tiles.First(t =>
            t.IsBuildable && map.GetTile(t.Coord.Neighbor(0)) is { IsBuildable: true }
                          && map.GetTile(t.Coord.Neighbor(1)) is { IsBuildable: true });
        var colony = new Colony();
        StaffedDrill(colony, map, drillTile.Coord);
        var catalog = BuildingCatalog.LoadDefault();
        var solar = new Building(catalog.Get("solar_panel"), drillTile.Coord.Neighbor(0), startOperational: true);      // Power
        var o2 = new Building(catalog.Get("o2_recycler"), drillTile.Coord.Neighbor(1), startOperational: true);        // LifeSupport
        colony.AddBuilding(solar);
        colony.AddBuilding(o2);
        var world = new World(map, colony, new ISimulationSystem[] { new SeismicSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 400; i++) world.Tick();

        Assert.Contains(world.EventNotifications, n => n.Contains("Marsquake")); // έγινε σεισμός
        Assert.Equal(BuildingState.Operational, solar.State);                   // ...αλλά η ενέργεια γλίτωσε
        Assert.Equal(BuildingState.Operational, o2.State);                      // ...και η υποστήριξη ζωής
    }

    [Fact]
    public void Deep_Core_Drill_Produces_Metals_Without_A_Deposit()
    {
        var map = Map();
        var colony = new Colony();
        StaffedDrill(colony, map, map.Tiles.First(t => t.IsBuildable).Coord);
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        var world = new World(map, colony, new ISimulationSystem[] { new ProductionSystem() });

        for (int i = 0; i < 10; i++) world.Tick();

        Assert.True(colony.Ledger.Get(ResourceKind.Materials) > 0);
        Assert.True(colony.Ledger.Get(ResourceKind.Silicon) > 0);
    }

    [Fact]
    public void Deep_Core_Drill_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var drill = BuildingCatalog.LoadDefault().Get("deep_core_drill");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(drill, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("core_mantle_penetration");
        Assert.True(colony.TryPlaceBuilding(drill, tile.Coord, map).Success);
    }

    [Fact]
    public void Seismic_Stress_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.SeismicStress = 10.0;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(10.0, loaded.SeismicStress, 3);
    }
}

public class Phase2BAutomationTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    // Ασυμπλήρωτο drill (Industry, MaxWorkers 1) → κανονικά eff 0· παράγει μόνο αν αυτοματοποιηθεί.
    private static Building UnstaffedDrill(HexMap map, int index) =>
        new(BuildingCatalog.LoadDefault().Get("deep_core_drill"), map.Tiles.ElementAt(index).Coord, startOperational: true);

    private static Building Hive(HexMap map, int index) =>
        new(BuildingCatalog.LoadDefault().Get("ai_drone_hive"), map.Tiles.ElementAt(index).Coord, startOperational: true);

    [Fact]
    public void Drone_Hive_Runs_Industry_Without_Crew()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(UnstaffedDrill(map, 0));
        colony.AddBuilding(Hive(map, 1));
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        var world = new World(map, colony, new ISimulationSystem[] { new AutomationSystem(), new ProductionSystem() });
        ToTargets(world);
        world.Tick();                       // → Φάση 2
        for (int i = 0; i < 10; i++) world.Tick();

        Assert.True(colony.Ledger.Get(ResourceKind.Materials) > 0); // το drone τρέχει το drill χωρίς πλήρωμα
        Assert.True(world.AutomationLevel > 0);
    }

    [Fact]
    public void Unstaffed_Industry_Stays_Idle_Without_A_Hive()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(UnstaffedDrill(map, 0)); // ΧΩΡΙΣ hive
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        var world = new World(map, colony, new ISimulationSystem[] { new AutomationSystem(), new ProductionSystem() });
        ToTargets(world);
        world.Tick();
        for (int i = 0; i < 10; i++) world.Tick();

        Assert.Equal(0, colony.Ledger.Get(ResourceKind.Materials), 6); // κανένα πλήρωμα, κανένα drone → αδρανές
    }

    [Fact]
    public void Automation_Capacity_Limits_Coverage()
    {
        var map = Map();
        var colony = new Colony();
        for (int i = 0; i < 6; i++) colony.AddBuilding(UnstaffedDrill(map, i));
        colony.AddBuilding(Hive(map, 6)); // capacity 4
        var world = new World(map, colony, new ISimulationSystem[] { new AutomationSystem() });
        ToTargets(world);
        world.Tick();
        world.Tick();

        Assert.Equal(4, colony.Buildings.Count(b => b.Automated)); // μόνο 4 από τα 6
        Assert.Equal(4.0 / 6.0, world.AutomationLevel, 3);
    }

    [Fact]
    public void Automation_Is_Inactive_Before_Phase2()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(UnstaffedDrill(map, 0));
        colony.AddBuilding(Hive(map, 1));
        colony.Ledger.Set(ResourceKind.Energy, 100_000);
        var world = new World(map, colony, new ISimulationSystem[] { new AutomationSystem(), new ProductionSystem() });
        for (int i = 0; i < 20; i++) world.Tick(); // ΟΧΙ terraformed

        Assert.Equal(0, colony.Ledger.Get(ResourceKind.Materials), 6);
        Assert.False(colony.Buildings[0].Automated);
    }

    [Fact]
    public void AI_Drone_Hive_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var hive = BuildingCatalog.LoadDefault().Get("ai_drone_hive");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(hive, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("automated_labor_swarms");
        Assert.True(colony.TryPlaceBuilding(hive, tile.Coord, map).Success);
    }
}

public class Phase2BWeatherTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    private static Building Op(HexMap map, string id, Hex at) =>
        new(BuildingCatalog.LoadDefault().Get(id), at, startOperational: true);

    // Πυκνή & υγρή ατμόσφαιρα (τροφοδοτεί καταιγίδες) — και terraformed ώστε να μπει σε Φάση 2.
    private static void ThickWet(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    [Fact]
    public void Storm_Builds_In_Thick_Wet_Atmosphere()
    {
        var map = Map();
        var world = new World(map, new Colony(), new ISimulationSystem[] { new WeatherSystem() });
        ThickWet(world);
        world.Tick(); // → Φάση 2
        for (int i = 0; i < 50; i++) world.Tick();

        Assert.True(world.StormStress > 0);
        Assert.True(world.StormLevel > 0);
    }

    [Fact]
    public void No_Storm_In_Thin_Dry_Atmosphere()
    {
        var map = Map();
        var world = new World(map, new Colony(), new ISimulationSystem[] { new WeatherSystem() });
        ThickWet(world);
        world.Tick(); // → Φάση 2 (latched)
        world.Planet.Restore(PlanetState.TargetTemperature, 5, PlanetState.TargetOxygen, 0, 0); // λεπτή & ξηρή
        for (int i = 0; i < 200; i++) world.Tick();

        Assert.Equal(0, world.StormStress, 6); // χωρίς πυκνό αέρα/ωκεανούς → καμία καταιγίδα
    }

    [Fact]
    public void Hurricane_Knocks_Out_Solar_Arrays()
    {
        var map = Map();
        var colony = new Colony();
        var solar = Op(map, "solar_panel", map.Tiles.First(t => t.IsBuildable).Coord);
        colony.AddBuilding(solar);
        var world = new World(map, colony, new ISimulationSystem[] { new WeatherSystem() });
        ThickWet(world);
        world.Tick();
        for (int i = 0; i < 900; i++) world.Tick(); // η ενέργεια καταιγίδας ξεπερνά το κατώφλι → hurricane

        Assert.Equal(BuildingState.Disabled, solar.State);
        Assert.Contains(world.EventNotifications, n => n.Contains("Hurricane"));
    }

    [Fact]
    public void Sea_Wall_Shields_Nearby_Buildings()
    {
        var map = Map();
        var wallTile = map.Tiles.First(t => t.IsBuildable && map.GetTile(t.Coord.Neighbor(0)) is { IsBuildable: true });
        var farTile = map.Tiles.First(t => t.IsBuildable && t.Coord.DistanceTo(wallTile.Coord) > 3);
        var colony = new Colony();
        colony.AddBuilding(Op(map, "sea_wall", wallTile.Coord));
        var shielded = Op(map, "solar_panel", wallTile.Coord.Neighbor(0)); // dist 1 <= radius 2
        var exposed = Op(map, "solar_panel", farTile.Coord);
        colony.AddBuilding(shielded);
        colony.AddBuilding(exposed);
        var world = new World(map, colony, new ISimulationSystem[] { new WeatherSystem() });
        ThickWet(world);
        world.Tick();
        for (int i = 0; i < 900; i++) world.Tick();

        Assert.Equal(BuildingState.Disabled, exposed.State);      // απροστάτευτο → σαρώθηκε
        Assert.Equal(BuildingState.Operational, shielded.State);  // κοντά σε sea wall → γλίτωσε
    }

    [Fact]
    public void Hurricane_Spares_Life_Support()
    {
        var map = Map();
        var lowTile = map.Tiles.Where(t => t.IsBuildable).OrderBy(t => t.Elevation).First(); // χαμηλότερο (flood-prone)
        var farTile = map.Tiles.First(t => t.IsBuildable && t.Coord.DistanceTo(lowTile.Coord) > 3);
        var colony = new Colony();
        var o2 = Op(map, "o2_recycler", lowTile.Coord);          // LifeSupport
        var exposed = Op(map, "solar_panel", farTile.Coord);
        colony.AddBuilding(o2);
        colony.AddBuilding(exposed);
        var world = new World(map, colony, new ISimulationSystem[] { new WeatherSystem() });
        ThickWet(world);
        world.Tick();
        for (int i = 0; i < 900; i++) world.Tick();

        Assert.Equal(BuildingState.Disabled, exposed.State);       // έγινε hurricane
        Assert.Equal(BuildingState.Operational, o2.State);         // ...αλλά η υποστήριξη ζωής γλίτωσε
    }

    [Fact]
    public void Sea_Wall_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var wall = BuildingCatalog.LoadDefault().Get("sea_wall");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(wall, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("storm_engineering");
        Assert.True(colony.TryPlaceBuilding(wall, tile.Coord, map).Success);
    }

    [Fact]
    public void Storm_Stress_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.StormStress = 7.5;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(7.5, loaded.StormStress, 3);
    }
}

public class Phase2BEcosystemTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 11 }).Generate();

    // Terraformed + βιόσφαιρα (biomass) ώστε να μπει σε Φάση 2 και να υπάρχει πίεση εισβολής.
    private static void Terraformed(World w, double biomass = 0.5) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, biomass);

    [Fact]
    public void Infestation_Builds_With_A_Lush_Biosphere()
    {
        var map = Map();
        var world = new World(map, new Colony(), new ISimulationSystem[] { new EcosystemSystem() });
        Terraformed(world, 0.5);
        world.Tick(); // → Φάση 2
        for (int i = 0; i < 100; i++) world.Tick();

        Assert.True(world.InfestationLevel > 0);
    }

    [Fact]
    public void No_Infestation_Before_Phase2()
    {
        var map = Map();
        var world = new World(map, new Colony(), new ISimulationSystem[] { new EcosystemSystem() });
        for (int i = 0; i < 100; i++) world.Tick();
        Assert.Equal(0, world.InfestationLevel, 6);
    }

    [Fact]
    public void Genetic_Vault_Suppresses_Infestation()
    {
        var map = Map();
        var colony = new Colony();
        colony.AddBuilding(new Building(BuildingCatalog.LoadDefault().Get("genetic_vault"),
            map.Tiles.First().Coord, startOperational: true));
        var world = new World(map, colony, new ISimulationSystem[] { new EcosystemSystem() });
        Terraformed(world, 0.0);          // χαμηλή πίεση + ισχυρή καταστολή
        world.Tick();                     // → Φάση 2
        world.InfestationLevel = 0.5;     // υπάρχουσα μόλυνση
        for (int i = 0; i < 100; i++) world.Tick();

        Assert.True(world.InfestationLevel < 0.5); // ο vault την υποχωρεί
    }

    [Fact]
    public void High_Infestation_Eats_Crops_And_Withers_Vegetation()
    {
        var map = Map();
        foreach (var t in map.Tiles.Where(t => t.Terrain == TerrainType.Flatland).Take(5))
            t.Terrain = TerrainType.Vegetation;
        int vegStart = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Food, 1000);
        var world = new World(map, colony, new ISimulationSystem[] { new EcosystemSystem() });
        Terraformed(world, 0.5);
        world.Tick();                 // → Φάση 2
        world.InfestationLevel = 0.8; // πάνω από το κατώφλι
        for (int i = 0; i < 60; i++) world.Tick();

        Assert.True(colony.Ledger.Get(ResourceKind.Food) < 1000);                        // τρώνε σοδειές
        Assert.True(map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation) < vegStart); // μαραίνουν βλάστηση
    }

    [Fact]
    public void Withering_Scales_With_Infestation_Severity()
    {
        // Regression: ceil(level × WitherTilesPerTick) πρέπει να ΚΛΙΜΑΚΩΝΕΤΑΙ, όχι να είναι πάντα 1.
        static int WitheredInOneTick(double level)
        {
            var map = Map();
            foreach (var t in map.Tiles.Where(t => t.Terrain == TerrainType.Flatland).Take(10))
                t.Terrain = TerrainType.Vegetation;
            int before = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

            var world = new World(map, new Colony(), new ISimulationSystem[] { new EcosystemSystem() });
            Terraformed(world, 0.5);
            world.Tick();                // → Φάση 2 (μόλυνση ~0, χωρίς wither)
            world.InfestationLevel = level;
            world.Tick();                // ένα tick withering
            return before - map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);
        }

        Assert.True(WitheredInOneTick(1.0) > WitheredInOneTick(0.31)); // η βαρύτητα έχει σημασία
    }

    [Fact]
    public void Genetic_Vault_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var vault = BuildingCatalog.LoadDefault().Get("genetic_vault");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(vault, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("ecological_engineering");
        Assert.True(colony.TryPlaceBuilding(vault, tile.Coord, map).Success);
    }

    [Fact]
    public void Infestation_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.InfestationLevel = 0.5;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(0.5, loaded.InfestationLevel, 3);
    }
}

public class Phase2BHyperloopTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 13 }).Generate();

    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static Building Place(Colony c, string id, Hex at) =>
        Add(c, new Building(Catalog.Get(id), at, startOperational: true));

    private static Building Add(Colony c, Building b) { c.AddBuilding(b); return b; }

    private static void Terraformed(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0.3);

    // Στήνει κόσμο μόνο με το HyperloopSystem, με πυρήνα (κάψουλα) στο (0,0), τον φέρνει στη Φάση 2
    // και τρέχει ένα ακόμη tick ώστε το HyperloopSystem να υπολογίσει τους LogisticsFactor.
    private static (World world, Colony colony) InPhase2()
    {
        var colony = new Colony();
        Place(colony, "landing_capsule", new Hex(0, 0));
        var world = new World(Map(), colony, new ISimulationSystem[] { new HyperloopSystem() });
        Terraformed(world);
        return (world, colony);
    }

    private static readonly int GraceTicks = (int)(GameClock.TicksPerSol * 3);

    private static void Settle(World world)
    {
        // Πέρα από το grace onset ώστε να ενεργοποιηθεί ο logistics περιορισμός.
        for (int i = 0; i < GraceTicks + 3; i++) world.Tick();
    }

    [Fact]
    public void Remote_Industry_Outpost_Runs_At_Penalty_Without_Hyperloop()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(10, 0)); // 10 hexes > RemoteRange
        Settle(world);
        Assert.Equal(0.5, mine.LogisticsFactor, 3);
    }

    [Fact]
    public void Local_Industry_Is_Never_Penalized()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(3, 0)); // κοντά στον πυρήνα
        Settle(world);
        Assert.Equal(1.0, mine.LogisticsFactor, 3);
    }

    [Fact]
    public void Hyperloop_Terminal_Restores_A_Remote_Outpost()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(10, 0));
        Place(colony, "hyperloop_terminal", new Hex(6, 0)); // 6 από πυρήνα (συνδέεται), 4 από ορυχείο (εξυπηρετεί)
        Settle(world);
        Assert.Equal(1.0, mine.LogisticsFactor, 3);
    }

    [Fact]
    public void Chained_Terminals_Extend_Reach_Across_The_Map()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(16, 0));
        Place(colony, "hyperloop_terminal", new Hex(6, 0));   // πυρήνας → A
        Place(colony, "hyperloop_terminal", new Hex(12, 0));  // A → B → ορυχείο
        Settle(world);
        Assert.Equal(1.0, mine.LogisticsFactor, 3);
    }

    [Fact]
    public void Isolated_Terminal_Does_Not_Help_If_Unconnected_To_Core()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(20, 0));
        Place(colony, "hyperloop_terminal", new Hex(20, 0)); // κοντά στο ορυχείο αλλά μακριά από τον πυρήνα (>6)
        Settle(world);
        Assert.Equal(0.5, mine.LogisticsFactor, 3); // ο κόμβος δεν αγκυρώνεται → δεν μεταδίδει
    }

    [Fact]
    public void Broken_Terminal_Blacks_Out_Its_Linked_Base()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(10, 0));
        var terminal = Place(colony, "hyperloop_terminal", new Hex(6, 0));
        Settle(world);
        Assert.Equal(1.0, mine.LogisticsFactor, 3); // συνδεδεμένο

        terminal.State = BuildingState.Disabled;    // «σπάει» από ακραίο καιρό
        world.Tick();
        Assert.Equal(0.5, mine.LogisticsFactor, 3); // blackout μέχρι να επισκευαστεί
    }

    [Fact]
    public void Grace_Period_Cushions_The_Phase2_Onset()
    {
        var (world, colony) = InPhase2();
        var mine = Place(colony, "iron_mine", new Hex(10, 0)); // απομακρυσμένο, χωρίς terminal
        world.Tick(); // → Φάση 2
        world.Tick(); // λίγο μέσα στο grace
        Assert.Equal(1.0, mine.LogisticsFactor, 3);           // δεν τιμωρείται ακόμη
        Assert.True(world.Phase2Ticks < GraceTicks);

        for (int i = 0; i < GraceTicks; i++) world.Tick();
        Assert.Equal(0.5, mine.LogisticsFactor, 3);           // μετά το grace: blackout
    }

    [Fact]
    public void Phase2Ticks_Round_Trips_Through_Save()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.Phase2Active = true;
        world.Phase2Ticks = 999;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(999, loaded.Phase2Ticks);
    }

    [Fact]
    public void No_Logistics_Penalty_Before_Phase2()
    {
        var colony = new Colony();
        Place(colony, "landing_capsule", new Hex(0, 0));
        var mine = Place(colony, "iron_mine", new Hex(10, 0));
        var world = new World(Map(), colony, new ISimulationSystem[] { new HyperloopSystem() });
        for (int i = 0; i < 10; i++) world.Tick();
        Assert.Equal(1.0, mine.LogisticsFactor, 3);
    }

    [Fact]
    public void Hyperloop_Terminal_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var terminal = Catalog.Get("hyperloop_terminal");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(terminal, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("maglev_propulsion");
        Assert.True(colony.TryPlaceBuilding(terminal, tile.Coord, map).Success);
    }
}

public class Phase2APlagueTests
{
    private static HexMap Map() =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = 17 }).Generate();

    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    // Terraformed· το water ορίζει αν ο πλανήτης είναι «πλημμυρισμένος» (>= 0.40 → πανώλη).
    private static World Flooded(Colony colony, double water = 0.45) =>
        Terraform(colony, water);

    private static World Terraform(Colony colony, double water)
    {
        var world = new World(Map(), colony, new ISimulationSystem[] { new PlagueSystem() });
        world.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, water, 0.3);
        return world;
    }

    private static Colonist AddDoctor(Colony c, string name = "Doc")
    {
        var d = new Colonist(name, Specialty.Doctor);
        c.Colonists.Add(d);
        return d;
    }

    [Fact]
    public void Plague_Spreads_On_A_Flooded_Planet()
    {
        var colony = new Colony();
        var world = Flooded(colony);
        for (int i = 0; i < 60; i++) world.Tick();

        Assert.True(world.PlagueSeverity > 0.1);
        Assert.True(world.PlagueEfficiency < 1.0);
        Assert.True(world.PlagueActive);
    }

    [Fact]
    public void No_Plague_Before_Phase2()
    {
        var colony = new Colony();
        var world = new World(Map(), colony, new ISimulationSystem[] { new PlagueSystem() }); // όχι terraformed
        for (int i = 0; i < 20; i++) world.Tick();

        Assert.Equal(0, world.PlagueSeverity, 6);
        Assert.Equal(1.0, world.PlagueEfficiency, 6);
        Assert.False(world.PlagueActive);
    }

    [Fact]
    public void Doctors_Suppress_The_Plague()
    {
        var colony = new Colony();
        AddDoctor(colony, "A"); AddDoctor(colony, "B"); AddDoctor(colony, "C"); // 3 × 0.004 > spread 0.004
        var world = Flooded(colony);
        world.Tick();                       // → Φάση 2
        world.PlagueSeverity = 0.5;         // υπάρχουσα επιδημία
        for (int i = 0; i < 40; i++) world.Tick();

        Assert.True(world.PlagueSeverity < 0.5); // οι γιατροί την υποχωρούν
    }

    [Fact]
    public void Staffed_Isolation_Hospital_Cures_The_Plague()
    {
        var colony = new Colony();
        var hospital = new Building(Catalog.Get("isolation_hospital"), new Hex(0, 0), startOperational: true);
        colony.AddBuilding(hospital);
        var medic = new Colonist("Nurse", Specialty.Engineer); // μη-Doctor: απομονώνει τη συνεισφορά του κτιρίου
        colony.Colonists.Add(medic);
        colony.Assign(medic, hospital);

        var world = Flooded(colony);
        world.Tick();
        world.PlagueSeverity = 0.5;
        for (int i = 0; i < 40; i++) world.Tick();

        Assert.True(world.PlagueSeverity < 0.5); // το στελεχωμένο νοσοκομείο θεραπεύει
    }

    [Fact]
    public void Unstaffed_Hospital_Does_Not_Cure()
    {
        var colony = new Colony();
        colony.AddBuilding(new Building(Catalog.Get("isolation_hospital"), new Hex(0, 0), startOperational: true)); // 0 workers
        var world = Flooded(colony);
        world.Tick();
        world.PlagueSeverity = 0.5;
        for (int i = 0; i < 20; i++) world.Tick();

        Assert.True(world.PlagueSeverity > 0.5); // χωρίς προσωπικό δεν έχει ιατρική ικανότητα → η πανώλη μεγαλώνει
    }

    [Fact]
    public void Plague_Recedes_When_Oceans_Drop_Below_Threshold()
    {
        var colony = new Colony();
        var world = Terraform(colony, PlanetState.TargetWater); // 0.30 < 0.40 → όχι πλέον υγρός
        world.Tick();
        world.PlagueSeverity = 0.5;
        for (int i = 0; i < 40; i++) world.Tick();

        Assert.True(world.PlagueSeverity < 0.5); // φυσική ύφεση όταν οι ωκεανοί υποχωρούν
    }

    [Fact]
    public void PlagueSeverity_Round_Trips_Through_Save()
    {
        var map = Map();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), System.Array.Empty<ISimulationSystem>());
        world.PlagueSeverity = 0.4;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), Catalog, sponsors, out _);

        Assert.Equal(0.4, loaded.PlagueSeverity, 3);
    }

    [Fact]
    public void Isolation_Hospital_Gated_By_Tech()
    {
        var map = Map();
        var colony = new Colony();
        colony.Ledger.Set(ResourceKind.Credits, 100_000);
        var hospital = Catalog.Get("isolation_hospital");
        var tile = map.Tiles.First(t => t.IsBuildable);

        Assert.False(colony.TryPlaceBuilding(hospital, tile.Coord, map).Success); // locked

        colony.Tech.Researched.Add("macro_epidemiology");
        Assert.True(colony.TryPlaceBuilding(hospital, tile.Coord, map).Success);
    }
}

// Παλινδρομήσεις για τα ευρήματα του adversarial review.
public class Phase2ReviewRegressionTests
{
    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    private static void ToTargets(World w) =>
        w.Planet.Restore(PlanetState.TargetTemperature, PlanetState.TargetPressure,
            PlanetState.TargetOxygen, PlanetState.TargetWater, 0);

    // Το runaway πρέπει να μειώνει την υγεία ΠΑΡΑ την ambient αναγέννηση του EventSystem (0.0015/tick).
    [Fact]
    public void Runaway_Decay_Overpowers_Ambient_Health_Regen()
    {
        var sponsor = SponsorCatalog.LoadDefault().Get("normal");
        var world = new World(Map(), new Colony(),
            new ISimulationSystem[] { new EventSystem(sponsor, 7), new Phase2System() });
        var colonist = new Colonist("A", Specialty.Engineer);
        world.Colony.Colonists.Add(colonist);
        ToTargets(world);
        world.Tick(); // → Φάση 2

        // Μέτριο overshoot (temp 20 → severity ~1.33): 0.008*1.33 ≈ 0.0106 > 0.0015 regen.
        world.Planet.Restore(20, PlanetState.TargetPressure, PlanetState.TargetOxygen, PlanetState.TargetWater, 0);
        for (int i = 0; i < 300; i++) world.Tick();

        Assert.True(world.RunawayActive);
        Assert.True(colonist.Health < 0.9); // η υγεία πέφτει, δεν την «καλύπτει» η αναγέννηση
    }

    // Τα tiles που «έκαψε» το runaway (πίσω σε Flatland) επιβιώνουν save/load — δεν ξαναγεννιούνται ως seed terrain.
    [Fact]
    public void Save_Persists_Terrain_Flipped_To_Flatland()
    {
        var map = Map();
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        var world = new World(map, new Colony(), Array.Empty<ISimulationSystem>());

        // Ένα tile που ΔΕΝ είναι Flatland στον αρχικό χάρτη, «καμένο» σε Flatland (vaporize/wither).
        var tile = map.Tiles.First(t => t.Terrain != TerrainType.Flatland && t.Terrain != TerrainType.Mountain);
        tile.Terrain = TerrainType.Flatland;

        var loaded = SaveSystem.Load(SaveSystem.ToJson(world, sponsors.Get("normal")), catalog, sponsors, out _);

        Assert.Equal(TerrainType.Flatland, loaded.Map.GetTile(tile.Coord)!.Terrain); // δεν επανήλθε σε seed terrain
    }

    // Παλιό (v2) ήδη-terraformed save: μπαίνει σιωπηλά στη Φάση 2, ΧΩΡΙΣ να ξαναπυροδοτεί τον εορτασμό.
    [Fact]
    public void Old_Terraformed_Save_Enters_Phase2_Silently()
    {
        var catalog = BuildingCatalog.LoadDefault();
        var sponsors = SponsorCatalog.LoadDefault();
        string json = @"{
            ""Version"": 2, ""Seed"": 5, ""Width"": 24, ""Height"": 24, ""Ticks"": 0,
            ""Speed"": ""Normal"", ""SponsorId"": ""normal"", ""Crew"": 0,
            ""Planet"": { ""Temperature"": 0, ""Pressure"": 10, ""Oxygen"": 15, ""Water"": 0.30, ""Biomass"": 0.5 },
            ""Resources"": {}, ""Capacities"": {},
            ""Tech"": { ""Researched"": [], ""Current"": null, ""Progress"": 0 },
            ""Buildings"": [], ""Colonists"": [], ""TileOverrides"": [], ""Events"": []
        }";

        var loaded = SaveSystem.Load(json, catalog, sponsors, out _);

        Assert.True(loaded.IsTerraformed);
        Assert.True(loaded.Phase2Active);                // μπήκε σιωπηλά στη Φάση 2
        Assert.False(loaded.Phase2CelebrationPending);   // ΧΩΡΙΣ popup εορτασμού
        Assert.True(loaded.Colony.Tech.Phase2Unlocked);
    }

    // Μετά από πλήρες μαράζωμα, η βλάστηση μπορεί να ξαναμεγαλώσει (η growth queue ξαναχτίζεται όταν αδειάσει).
    [Fact]
    public void Withered_Vegetation_Regrows_After_Queue_Drains()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 14, Height = 14, Seed = 3 }).Generate();
        var colony = new Colony();
        var forest = new Building(BuildingCatalog.LoadDefault().Get("gm_forest"),
            map.Tiles.First().Coord, startOperational: true);
        colony.AddBuilding(forest);
        var botanist = new Colonist("B", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.Assign(botanist, forest);
        var world = new World(map, colony, new ISimulationSystem[] { new BiosphereSystem() });

        // Πλήρης πρασινάδα μέχρι να αδειάσει η ουρά (plateau).
        world.Planet.Restore(10, 5, 1, 0.3, 0);
        for (int i = 0; i < 8000; i++) world.Tick();
        int peak = map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);
        for (int i = 0; i < 200; i++) world.Tick();
        Assert.Equal(peak, map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation)); // η ουρά άδειασε
        Assert.True(peak > 0);

        // Μαράζωμα όλης της βλάστησης σε Flatland (runaway).
        foreach (var t in map.Tiles.Where(t => t.Terrain == TerrainType.Vegetation).ToList())
            t.Terrain = TerrainType.Flatland;
        Assert.Equal(0, map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation));

        // Συνέχεια: με το rebuild της ουράς, η βλάστηση επανέρχεται.
        for (int i = 0; i < 8000; i++) world.Tick();
        Assert.True(map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation) > 0);
    }
}
