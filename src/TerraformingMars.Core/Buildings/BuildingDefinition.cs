using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;
using TerraformingMars.Core.Simulation;

namespace TerraformingMars.Core.Buildings;

/// <summary>
/// Στατικός ορισμός τύπου κτιρίου (data-driven, από JSON). Ένα instance είναι <see cref="Building"/>.
/// </summary>
public sealed class BuildingDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    /// <summary>Σύντομη περιγραφή (Αγγλικά) — εμφανίζεται στο help της παλέτας κτιρίων.</summary>
    public string Description { get; init; } = "";

    public string Category { get; init; } = "General";

    /// <summary>Αν false, δεν τοποθετείται από τον παίκτη (π.χ. κάψουλα προσεδάφισης).</summary>
    public bool Buildable { get; init; } = true;

    public int BuildTimeTicks { get; init; } = 100;

    /// <summary>0 = αυτόματο (δεν χρειάζεται προσωπικό). &gt;0 = χρειάζεται αποίκους για να λειτουργήσει.</summary>
    public int MaxWorkers { get; init; } = 0;

    public Specialty OptimalSpecialty { get; init; } = Specialty.None;

    /// <summary>Αν != None, το hex πρέπει να έχει αυτό το κοίτασμα (π.χ. ice drill → Ice).</summary>
    public ResourceType RequiresDeposit { get; init; } = ResourceType.None;

    /// <summary>Μονάδες κοιτάσματος που εξορύσσονται/tick (>0 ⇒ ορυχείο· η παραγωγή σταματά όταν εξαντληθεί).</summary>
    public double ExtractionPerTick { get; init; } = 0.0;

    /// <summary>Μονάδες αποθηκευμένου Silicon που πωλούνται σε Credits/tick (>0 ⇒ export terminal· το χειρίζεται το <see cref="Simulation.MarketSystem"/>).</summary>
    public double ExportPerTick { get; init; } = 0.0;

    /// <summary>Τιμή Credits ανά μονάδα Silicon για ΑΥΤΟ το κτίριο (0 = παγκόσμια τιμή). >global ⇒ επεξεργασία (quantum).</summary>
    public double SiliconExportPrice { get; init; } = 0.0;

    /// <summary>Μονάδες πλεονάζοντος Materials (πάνω από reserve) που πωλούνται/tick (stock exchange).</summary>
    public double MaterialsExportPerTick { get; init; } = 0.0;

    /// <summary>Τεχνολογία που απαιτείται για να ξεκλειδωθεί (κενό = διαθέσιμο από την αρχή).</summary>
    public string RequiredTech { get; init; } = "";

    /// <summary>Επιτρεπτά terrain. Κενό = οποιοδήποτε buildable.</summary>
    public List<TerrainType> AllowedTerrain { get; init; } = new();

    /// <summary>Εφάπαξ κόστος τοποθέτησης.</summary>
    public Dictionary<ResourceKind, double> Cost { get; init; } = new();

    /// <summary>Καθαρή παραγωγή/κατανάλωση ανά tick όταν λειτουργεί (θετικό=παραγωγή).</summary>
    public Dictionary<ResourceKind, double> Production { get; init; } = new();

    /// <summary>Χωρητικότητα αποθήκευσης που προσθέτει όταν γίνει operational.</summary>
    public Dictionary<ResourceKind, double> Storage { get; init; } = new();

    /// <summary>Επίδραση στις πλανητικές μετρικές ανά tick (macro-engineering· Water = tiles/tick για πλημμύρα).</summary>
    public Dictionary<PlanetMetric, double> PlanetEffects { get; init; } = new();

    /// <summary>Αν true, προστατεύει την ατμόσφαιρα (τεχνητή μαγνητόσφαιρα) — σταματά την απώλεια πίεσης.</summary>
    public bool ShieldsAtmosphere { get; init; } = false;

    /// <summary>Αν true, η ενεργειακή του παραγωγή επηρεάζεται από αμμοθύελλες (ηλιακά panel).</summary>
    public bool SolarPowered { get; init; } = false;

    /// <summary>Tiles βλάστησης που απλώνει/tick (βιόσφαιρα — απαιτεί ζεστασιά & νερό).</summary>
    public double VegetationSpreadPerTick { get; init; } = 0.0;

    /// <summary>Πόσους αποίκους μπορεί να στεγάσει (όριο επώνυμου πληθυσμού).</summary>
    public int HousingCapacity { get; init; } = 0;

    /// <summary>Χωρητικότητα αφηρημένου πληθυσμού Φάσης 2 (arcologies) — ξεχωριστή από το <see cref="HousingCapacity"/>.</summary>
    public int PopulationCapacity { get; init; } = 0;

    /// <summary>Ελάχιστος αφηρημένος πληθυσμός για ξεκλείδωμα (threshold gate, π.χ. arcology στα 10.000). 0 = χωρίς όριο.</summary>
    public int RequiresPopulation { get; init; } = 0;

    /// <summary>Ρύπανση που εκπέμπει/tick στο hex του (Φάση 2, βαριά βιομηχανία). 0 = καθαρό.</summary>
    public double PollutionPerTick { get; init; } = 0.0;

    /// <summary>Σεισμική αστάθεια που προσθέτει/tick (Φάση 2B, deep core drilling). Υψηλή συσσώρευση → marsquake.</summary>
    public double SeismicPerTick { get; init; } = 0.0;

    /// <summary>Αν true, καθαρίζει ρύπανση στο hex του και στα γειτονικά (μονάδα αντιρρύπανσης).</summary>
    public bool ScrubsPollution { get; init; } = false;

    /// <summary>Αν true, τοποθετείται μόνο δίπλα σε υπάρχον κτίριο-κατοικία (Category "Habitat") —
    /// ώστε οι νέες κατοικίες να παραμένουν συνδεδεμένες με το δίκτυο της κάψουλας προσγείωσης.</summary>
    public bool RequiresHabitatLink { get; init; } = false;
}

