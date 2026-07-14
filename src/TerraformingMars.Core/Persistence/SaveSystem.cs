using System.Globalization;
using System.Text.Json;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;

namespace TerraformingMars.Core.Persistence;

/// <summary>Αποθήκευση/φόρτωση παιχνιδιού σε JSON. Ο χάρτης αναγεννιέται από το seed και
/// εφαρμόζονται μόνο τα tiles που άλλαξαν (νερό/βλάστηση/εξορυγμένα κοιτάσματα).</summary>
public static class SaveSystem
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string ToJson(World world, SponsorProfile sponsor, string? name = null, DateTime? savedAtUtc = null)
    {
        var save = Capture(world, sponsor);
        save.Name = name ?? "";
        save.SavedAtUtc = (savedAtUtc ?? DateTime.UtcNow).ToString("o", CultureInfo.InvariantCulture);
        return JsonSerializer.Serialize(save, Options);
    }

    /// <summary>Διαβάζει μόνο τα metadata (όνομα + τοπική ώρα) ενός save χωρίς πλήρη φόρτωση κόσμου.</summary>
    public static (string Name, DateTime SavedAtLocal) ReadInfo(string json)
    {
        var save = JsonSerializer.Deserialize<SaveGame>(json, Options);
        if (save is null) return ("", DateTime.MinValue);
        return DateTime.TryParse(save.SavedAtUtc, CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var dt)
            ? (save.Name, dt.ToLocalTime())
            : (save.Name, DateTime.MinValue);
    }

    public static SaveGame Capture(World world, SponsorProfile sponsor)
    {
        var colony = world.Colony;
        var p = world.Planet;

        // Terrain που έχει αλλάξει από τον αρχικό (seed-generated) χάρτη — ώστε ΟΛΕΣ οι μετατροπές
        // tiles (λιώσιμο/πλημμύρα/βλάστηση ΚΑΙ εξάτμιση/μαράζωμα της Φάσης 2 πίσω σε Flatland) να
        // επιβιώνουν του load. Αλλιώς ένα vaporized/withered tile ξαναγεννιέται ως seed terrain
        // (π.χ. PolarIce) και ξαναλιώνει, «θεραπεύοντας» σιωπηλά τη ζημιά του runaway.
        var pristine = new MapGenerator(new MapGenerationSettings
            { Width = world.Map.Width, Height = world.Map.Height, Seed = world.Map.Seed })
            .Generate().Tiles.ToDictionary(t => t.Coord, t => t.Terrain);

        return new SaveGame
        {
            Seed = world.Map.Seed,
            Width = world.Map.Width,
            Height = world.Map.Height,
            Ticks = world.Clock.TotalTicks,
            Speed = world.Clock.Speed.ToString(),
            SponsorId = sponsor.Id,
            Crew = colony.Crew,
            Population = colony.Population,
            PeakPopulation = colony.PeakPopulation,
            Phase2Active = world.Phase2Active,
            Phase2Ticks = world.Phase2Ticks,
            UrbanizationReached = world.UrbanizationReached,
            IndustrialShiftReached = world.IndustrialShiftReached,
            SeismicStress = world.SeismicStress,
            StormStress = world.StormStress,
            InfestationLevel = world.InfestationLevel,
            IndustrialistApproval = colony.IndustrialistApproval,
            EcologistApproval = colony.EcologistApproval,
            HasCaveShelter = world.HasCaveShelter,
            SolarEfficiency = world.SolarEfficiency,
            Planet = new PlanetSave
            {
                Temperature = p.Temperature, Pressure = p.Pressure, Oxygen = p.Oxygen,
                Water = p.WaterCoverage, Biomass = p.Biomass
            },
            Resources = Enum.GetValues<ResourceKind>().ToDictionary(k => k.ToString(), colony.Ledger.Get),
            Capacities = Enum.GetValues<ResourceKind>().Where(colony.Ledger.HasCapacityLimit)
                             .ToDictionary(k => k.ToString(), colony.Ledger.Capacity),
            Tech = new TechSave
            {
                Researched = colony.Tech.Researched.ToList(),
                Current = colony.Tech.CurrentTarget,
                Progress = colony.Tech.CurrentProgress,
                Phase2Unlocked = colony.Tech.Phase2Unlocked
            },
            Buildings = colony.Buildings.Select(b => new BuildingSave
            {
                Id = b.Definition.Id, Q = b.Location.Q, R = b.Location.R, State = b.State.ToString(),
                BuildProgress = b.BuildProgress, MaterialsPaid = b.MaterialsPaid, Stalled = b.Stalled,
                DepositDepleted = b.DepositDepleted, RepairTicksRemaining = b.RepairTicksRemaining,
                CreatedTick = b.CreatedTick
            }).ToList(),
            Colonists = colony.Colonists.Select(c => new ColonistSave
            {
                Name = c.Name, Specialty = c.Specialty.ToString(), Health = c.Health, Morale = c.Morale,
                AssignmentIndex = c.Assignment is null ? -1 : colony.Buildings.IndexOf(c.Assignment)
            }).ToList(),
            TileOverrides = world.Map.Tiles
                .Where(t => (pristine.TryGetValue(t.Coord, out var terr) && t.Terrain != terr)
                            || t.RemainingDeposit < t.Deposit.Amount - 1e-6
                            || t.Pollution > 1e-6)
                .Select(t => new TileSave
                {
                    Q = t.Coord.Q, R = t.Coord.R, Terrain = t.Terrain.ToString(),
                    Remaining = t.RemainingDeposit, Pollution = t.Pollution
                }).ToList(),
            Events = world.ActiveEvents.Select(e => new EventSave
            {
                Type = e.Type.ToString(), TicksRemaining = e.TicksRemaining
            }).ToList()
        };
    }

    public static World Load(string json, BuildingCatalog catalog, SponsorCatalog sponsors, out SponsorProfile sponsor)
    {
        var save = JsonSerializer.Deserialize<SaveGame>(json, Options)
                   ?? throw new InvalidDataException("Άκυρο αρχείο αποθήκευσης.");

        var map = new MapGenerator(new MapGenerationSettings { Width = save.Width, Height = save.Height, Seed = save.Seed }).Generate();
        var byCoord = map.Tiles.ToDictionary(t => t.Coord);
        foreach (var ts in save.TileOverrides)
            if (byCoord.TryGetValue(new Hex(ts.Q, ts.R), out var tile))
            {
                tile.Terrain = Enum.Parse<TerrainType>(ts.Terrain);
                tile.RemainingDeposit = ts.Remaining;
                tile.Pollution = ts.Pollution;
            }

        var colony = new Colony();
        foreach (var (k, cap) in save.Capacities) colony.Ledger.AddCapacity(Enum.Parse<ResourceKind>(k), cap);
        foreach (var (k, amt) in save.Resources) colony.Ledger.Set(Enum.Parse<ResourceKind>(k), amt);
        colony.Tech.Restore(save.Tech.Researched, save.Tech.Current, save.Tech.Progress, save.Tech.Phase2Unlocked);
        colony.Crew = save.Crew;
        colony.Population = save.Population;
        colony.PeakPopulation = save.PeakPopulation;
        colony.IndustrialistApproval = save.IndustrialistApproval;
        colony.EcologistApproval = save.EcologistApproval;

        foreach (var bs in save.Buildings)
        {
            var b = new Building(catalog.Get(bs.Id), new Hex(bs.Q, bs.R))
            {
                State = Enum.Parse<BuildingState>(bs.State),
                BuildProgress = bs.BuildProgress,
                MaterialsPaid = bs.MaterialsPaid,
                Stalled = bs.Stalled,
                DepositDepleted = bs.DepositDepleted,
                RepairTicksRemaining = bs.RepairTicksRemaining,
                CreatedTick = bs.CreatedTick
            };
            colony.Buildings.Add(b);
        }

        foreach (var cs in save.Colonists)
        {
            var c = new Colonist(cs.Name, Enum.Parse<Specialty>(cs.Specialty)) { Health = cs.Health, Morale = cs.Morale };
            colony.Colonists.Add(c);
            if (cs.AssignmentIndex >= 0 && cs.AssignmentIndex < colony.Buildings.Count)
            {
                var b = colony.Buildings[cs.AssignmentIndex];
                b.Workers.Add(c);
                c.Assignment = b;
            }
        }

        sponsor = sponsors.Get(save.SponsorId);
        colony.BaseHousing = sponsor.BaseHousing;
        var systems = new ISimulationSystem[]
        {
            new EventSystem(sponsor, map.Seed),
            new ConstructionSystem(),
            new AutomationSystem(),
            new HyperloopSystem(),
            new ProductionSystem(),
            new MarketSystem(),
            new ResearchSystem(),
            new PlanetSystem(),
            new BiosphereSystem(),
            new Phase2System(),
            new PopulationSystem(map.Seed),
            new LifeSupportSystem(),
            new SocietySystem(),
            new PollutionSystem(),
            new SeismicSystem(),
            new WeatherSystem(),
            new EcosystemSystem(),
            new FactionSystem()
        };

        var world = new World(map, colony, systems);
        world.Planet.Restore(save.Planet.Temperature, save.Planet.Pressure, save.Planet.Oxygen, save.Planet.Water, save.Planet.Biomass);
        world.Clock.RestoreTicks(save.Ticks);
        world.Clock.Speed = Enum.Parse<GameSpeed>(save.Speed);
        world.HasCaveShelter = save.HasCaveShelter;
        world.SolarEfficiency = save.SolarEfficiency;
        world.Phase2Active = save.Phase2Active;
        world.Phase2Ticks = save.Phase2Ticks;
        world.UrbanizationReached = save.UrbanizationReached;
        world.IndustrialShiftReached = save.IndustrialShiftReached;
        world.SeismicStress = save.SeismicStress;
        world.StormStress = save.StormStress;
        world.InfestationLevel = save.InfestationLevel;

        // Παλιό (προ-Φάσης-2) save που ήταν ήδη terraformed: μπες σιωπηλά στη Φάση 2 κατά το load,
        // ώστε το World.Tick να ΜΗΝ ξαναπυροδοτήσει τον εορτασμό/chime στο πρώτο tick.
        if (!world.Phase2Active && world.Planet.IsTerraformed)
        {
            world.Phase2Active = true;
            colony.Tech.UnlockPhase2();
            if (colony.Population < World.Phase2StartingPopulation)
                colony.Population = World.Phase2StartingPopulation;
        }

        foreach (var es in save.Events)
            world.ActiveEvents.Add(new ActiveEvent(Enum.Parse<EventType>(es.Type), es.TicksRemaining));

        return world;
    }
}
