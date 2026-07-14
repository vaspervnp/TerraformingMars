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

    /// <summary>True όταν χαθεί όλο το πλήρωμα (κατάρρευση life support) — ήττα.</summary>
    public bool IsLost => Colony.Collapsed;

    /// <summary>Αυξάνεται όταν αλλάζει το terrain (π.χ. πάγος→νερό), ώστε το rendering να ξαναχτίσει τον χάρτη.</summary>
    public int MapRevision { get; private set; }
    internal void BumpMapRevision() => MapRevision++;

    // --- Κατάσταση γεγονότων (Φάση 6) ---
    public double SolarEfficiency { get; internal set; } = 1.0;   // 1 = κανονικά, <1 σε αμμοθύελλα
    public bool HasCaveShelter { get; internal set; }             // θωράκιση από ακτινοβολία
    public bool PowerOutage { get; internal set; }                // ενημερωτικό (αποθηκευμένη ενέργεια = 0)
    public List<ActiveEvent> ActiveEvents { get; } = new();
    public List<string> EventNotifications { get; } = new();      // πρόσφατα μηνύματα για το HUD

    // --- Phase 2: The Living Planet ---
    /// <summary>Ξεκίνησε η Φάση 2 (μετά την ολοκλήρωση του terraforming). Latched — σώζεται, δεν επανέρχεται.</summary>
    public bool Phase2Active { get; internal set; }
    /// <summary>Εφήμερο (δεν σώζεται): true όσο η ατμόσφαιρα ξεπερνά τον στόχο σε runaway greenhouse.</summary>
    public bool RunawayActive { get; internal set; }
    /// <summary>Εφήμερο one-shot (δεν σώζεται): το UI το «καταναλώνει» για το popup εορτασμού της Φάσης 2.</summary>
    public bool Phase2CelebrationPending { get; internal set; }

    /// <summary>Καταναλώνει (μία φορά) το σήμα εορτασμού Φάσης 2 από το UI. True μόλις μπήκε στη Φάση 2.</summary>
    public bool ConsumePhase2Celebration()
    {
        if (!Phase2CelebrationPending) return false;
        Phase2CelebrationPending = false;
        return true;
    }
    /// <summary>Αρχικός πληθυσμός τη στιγμή της μετάβασης στη Φάση 2.</summary>
    public const double Phase2StartingPopulation = 1000;

    /// <summary>Πλήθος ticks που έχει τρέξει η Φάση 2 (σώζεται). Ανεξάρτητο από το ρολόι ώστε να
    /// προχωρά ντετερμινιστικά και στα tests. Το χρησιμοποιεί το <see cref="HyperloopSystem"/> για το
    /// grace του logistics onset (δεν στραγγαλίζει απομακρυσμένα outposts αμέσως μόλις μπει η Φάση 2).</summary>
    public long Phase2Ticks { get; internal set; }

    // --- Phase 2A: Κοινωνία & πληθυσμιακή κλίμακα ---
    /// <summary>Εφήμερο (δεν σώζεται): true όταν ο πληθυσμός ξεπερνά τη στέγαση ή λείπουν πόροι (systemic stagnation).</summary>
    public bool StagnationActive { get; internal set; }
    /// <summary>Εφήμερο (δεν σώζεται): πολλαπλασιαστής παραγωγής (1.0 κανονικά, &lt;1 σε stagnation). Το διαβάζει το ProductionSystem.</summary>
    public double ProductionEfficiency { get; internal set; } = 1.0;
    /// <summary>Latched (σώζεται): έχει φτάσει το κατώφλι Urbanization (10.000) — ξεκλειδώνει το arcology.</summary>
    public bool UrbanizationReached { get; internal set; }
    /// <summary>Εφήμερο one-shot (δεν σώζεται): το UI το «καταναλώνει» για την ειδοποίηση Urbanization.</summary>
    public bool UrbanizationPending { get; internal set; }
    /// <summary>Πληθυσμιακό κατώφλι της εποχής Urbanization (Threshold 1).</summary>
    public const int UrbanizationThreshold = 10000;

    /// <summary>Καταναλώνει (μία φορά) το σήμα Urbanization από το UI.</summary>
    public bool ConsumeUrbanization()
    {
        if (!UrbanizationPending) return false;
        UrbanizationPending = false;
        return true;
    }

    // --- Phase 2B: The Industrial Shift (Threshold 2, 50k) ---
    /// <summary>Latched (σώζεται): έφτασε το κατώφλι 50k — εποχή Industrial Shift (macro-economy).</summary>
    public bool IndustrialShiftReached { get; internal set; }
    /// <summary>Εφήμερο one-shot (δεν σώζεται): το UI το «καταναλώνει» για την ειδοποίηση Industrial Shift.</summary>
    public bool IndustrialShiftPending { get; internal set; }
    /// <summary>Πληθυσμιακό κατώφλι της εποχής Industrial Shift (Threshold 2).</summary>
    public const int IndustrialShiftThreshold = 50000;

    /// <summary>Καταναλώνει (μία φορά) το σήμα Industrial Shift από το UI.</summary>
    public bool ConsumeIndustrialShift()
    {
        if (!IndustrialShiftPending) return false;
        IndustrialShiftPending = false;
        return true;
    }

    // --- Phase 2A: Πολιτικές παρατάξεις (factions) ---
    /// <summary>Εφήμερο (δεν σώζεται): οι Βιομηχανικοί απεργούν — τα κτίρια κατηγορίας "Industry" σταματούν.</summary>
    public bool IndustrialStrike { get; internal set; }
    /// <summary>Εφήμερο (δεν σώζεται): οι Οικολόγοι απεργούν — τα κτίρια κατηγορίας "Biosphere" σταματούν.</summary>
    public bool EcologistStrike { get; internal set; }

    /// <summary>True αν το κτίριο είναι σε απεργία της παράταξής του (σταματά παραγωγή/επιδράσεις/βλάστηση).</summary>
    public bool IsOnStrike(Buildings.BuildingDefinition def) =>
        (IndustrialStrike && def.Category == "Industry") ||
        (EcologistStrike && def.Category == "Biosphere");

    /// <summary>Εφήμερο (δεν σώζεται): συνολικός δείκτης ρύπανσης 0..1 — τον διαβάζουν HUD & FactionSystem.</summary>
    public double PollutionLevel { get; internal set; }

    // --- Phase 2B: Σεισμική αστάθεια (deep core extraction) ---
    /// <summary>Συσσωρευμένη σεισμική αστάθεια (σώζεται)· όταν ξεπεράσει το κατώφλι → marsquake.</summary>
    public double SeismicStress { get; internal set; }
    /// <summary>Εφήμερο (δεν σώζεται): 0..1 δείκτης εγγύτητας σε marsquake — για το HUD.</summary>
    public double SeismicLevel { get; internal set; }

    /// <summary>Εφήμερο (δεν σώζεται): ποσοστό 0..1 των βιομηχανικών κτιρίων που τρέχουν με AI drones (Φάση 2B).</summary>
    public double AutomationLevel { get; internal set; }

    // --- Phase 2B: Ακραίος καιρός (super-storms) ---
    /// <summary>Συσσωρευμένη ενέργεια καταιγίδας (σώζεται)· όταν ξεπεράσει το κατώφλι → hurricane.</summary>
    public double StormStress { get; internal set; }
    /// <summary>Εφήμερο (δεν σώζεται): 0..1 δείκτης εγγύτητας σε hurricane — για το HUD.</summary>
    public double StormLevel { get; internal set; }

    // --- Phase 2B: Οικοσύστημα & εισβλητικά είδη ---
    /// <summary>Επίπεδο μόλυνσης από εισβλητικά είδη 0..1 (σώζεται)· υψηλό → τρώει σοδειές & μαραίνει βλάστηση.</summary>
    public double InfestationLevel { get; internal set; }

    // --- Phase 2B: Δίκτυο Hyperloop (logistics) ---
    /// <summary>Εφήμερο (δεν σώζεται): πλήθος απομακρυσμένων βιομηχανικών outposts που είναι ασύνδετα από
    /// το δίκτυο Hyperloop και τρέχουν με μειωμένη παραγωγή (blackout) — για το HUD.</summary>
    public int LogisticsBlackoutCount { get; internal set; }

    /// <summary>Γεγονότα που μόλις ξεκίνησαν αυτό το βήμα· τα «καταναλώνει» το UI για popup (εφήμερο, δεν σώζεται).</summary>
    public List<EventStart> StartedEvents { get; } = new();

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

        // Μετάβαση στη Φάση 2 (μία φορά, στην ακμή ανόδου του terraforming). Latched: δεν επανέρχεται
        // αν αργότερα κάποια μετρική πέσει κάτω από τον στόχο (π.χ. λόγω του cryo-carbon sink).
        if (!Phase2Active && Planet.IsTerraformed)
        {
            Phase2Active = true;
            Phase2CelebrationPending = true;
            Colony.Tech.UnlockPhase2();
            if (Colony.Population < Phase2StartingPopulation) Colony.Population = Phase2StartingPopulation;
        }

        if (Phase2Active) Phase2Ticks++;
    }
}
