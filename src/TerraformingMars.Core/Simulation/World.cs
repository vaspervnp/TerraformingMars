using TerraformingMars.Core.Events;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Η ρίζα της κατάστασης παιχνιδιού: χάρτης + αποικία + ρολόι + simulation systems.
/// Το <see cref="Update"/> καλείται από το render loop· η σιμουλασιόν τρέχει σε σταθερά ticks.
/// </summary>
public sealed class World
{
    private static readonly ResourceKind[] AllResources = Enum.GetValues<ResourceKind>();

    private readonly List<ISimulationSystem> _systems;
    private readonly Dictionary<ResourceKind, double> _before = new();

    public HexMap Map { get; }
    public Colony Colony { get; }
    public PlanetState Planet { get; } = new();
    public GameClock Clock { get; } = new();

    /// <summary>True όταν και οι 4 πλανητικές μετρικές φτάσουν τους στόχους — νίκη.</summary>
    public bool IsTerraformed => Planet.IsTerraformed;

    /// <summary>Αυξάνεται όταν αλλάζει το terrain (π.χ. πάγος→νερό), ώστε το rendering να ξαναχτίσει τον χάρτη.</summary>
    public int MapRevision { get; private set; }
    internal void BumpMapRevision() => MapRevision++;

    // --- Κατάσταση γεγονότων (Φάση 6) ---
    public double SolarEfficiency { get; internal set; } = 1.0;   // 1 = κανονικά, <1 σε αμμοθύελλα
    public bool HasCaveShelter { get; internal set; }             // θωράκιση από ακτινοβολία
    public bool PowerOutage { get; internal set; }                // ενημερωτικό (αποθηκευμένη ενέργεια = 0)
    public List<ActiveEvent> ActiveEvents { get; } = new();
    public List<string> EventNotifications { get; } = new();      // πρόσφατα μηνύματα για το HUD

    public World(HexMap map, Colony colony, IEnumerable<ISimulationSystem>? systems = null)
    {
        Map = map;
        Colony = colony;
        _systems = systems?.ToList() ?? new List<ISimulationSystem>
        {
            new ProductionSystem(),
            new LifeSupportSystem()
        };
    }

    /// <summary>Προωθεί τη σιμουλασιόν με βάση τον πραγματικό χρόνο. Επιστρέφει πόσα ticks έτρεξαν.</summary>
    public int Update(double realDeltaSeconds)
    {
        int ticks = Clock.Advance(realDeltaSeconds);
        for (int i = 0; i < ticks; i++) Tick();
        return ticks;
    }

    /// <summary>Ένα διακριτό βήμα σιμουλασιόν. Καταγράφει net rate/tick μέσω snapshot πριν/μετά.</summary>
    public void Tick()
    {
        var ledger = Colony.Ledger;
        foreach (var k in AllResources) _before[k] = ledger.Get(k);

        foreach (var system in _systems) system.Tick(this);

        foreach (var k in AllResources) ledger.RecordRate(k, ledger.Get(k) - _before[k]);
    }
}
