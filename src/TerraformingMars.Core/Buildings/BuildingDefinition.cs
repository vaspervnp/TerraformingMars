using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;

namespace TerraformingMars.Core.Buildings;

/// <summary>
/// Στατικός ορισμός τύπου κτιρίου (data-driven, από JSON). Ένα instance είναι <see cref="Building"/>.
/// </summary>
public sealed class BuildingDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "General";

    /// <summary>Αν false, δεν τοποθετείται από τον παίκτη (π.χ. κάψουλα προσεδάφισης).</summary>
    public bool Buildable { get; init; } = true;

    public int BuildTimeTicks { get; init; } = 100;

    /// <summary>0 = αυτόματο (δεν χρειάζεται προσωπικό). &gt;0 = χρειάζεται αποίκους για να λειτουργήσει.</summary>
    public int MaxWorkers { get; init; } = 0;

    public Specialty OptimalSpecialty { get; init; } = Specialty.None;

    /// <summary>Αν != None, το hex πρέπει να έχει αυτό το κοίτασμα (π.χ. ice drill → Ice).</summary>
    public ResourceType RequiresDeposit { get; init; } = ResourceType.None;

    /// <summary>Επιτρεπτά terrain. Κενό = οποιοδήποτε buildable.</summary>
    public List<TerrainType> AllowedTerrain { get; init; } = new();

    /// <summary>Εφάπαξ κόστος τοποθέτησης.</summary>
    public Dictionary<ResourceKind, double> Cost { get; init; } = new();

    /// <summary>Καθαρή παραγωγή/κατανάλωση ανά tick όταν λειτουργεί (θετικό=παραγωγή).</summary>
    public Dictionary<ResourceKind, double> Production { get; init; } = new();

    /// <summary>Χωρητικότητα αποθήκευσης που προσθέτει όταν γίνει operational.</summary>
    public Dictionary<ResourceKind, double> Storage { get; init; } = new();
}
