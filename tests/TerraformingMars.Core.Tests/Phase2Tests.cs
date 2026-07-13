using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
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

        Assert.False(colony.TryPlaceBuilding(arco, tile.Coord, map).Success); // pop 0 < 10000

        colony.Population = 10_000;
        Assert.True(colony.TryPlaceBuilding(arco, tile.Coord, map).Success);
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
