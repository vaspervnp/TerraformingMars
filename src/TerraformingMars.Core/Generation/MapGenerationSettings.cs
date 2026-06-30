namespace TerraformingMars.Core.Generation;

/// <summary>
/// Παράμετροι procedural generation. Όλα tunable — αργότερα θα φορτώνονται από JSON
/// ανά επίπεδο δυσκολίας / sponsor (πλούσια vs φτωχά κοιτάσματα κ.λπ.).
/// </summary>
public sealed class MapGenerationSettings
{
    public int Width { get; init; } = 60;
    public int Height { get; init; } = 40;
    public int Seed { get; init; } = 1337;

    // --- Noise ---
    public float NoiseScale { get; init; } = 0.08f;  // μικρότερο = μεγαλύτερες ήπειροι
    public int Octaves { get; init; } = 5;
    public float Lacunarity { get; init; } = 2.0f;
    public float Persistence { get; init; } = 0.5f;

    // --- Κατανομή εδάφους (σωρευτικά quantiles του υψομέτρου, εκτός πόλων) ---
    // Εγγυάται σταθερή ποικιλία ανεξάρτητα από το «εύρος» του θορύβου.
    // Προκύπτουν: Canyon 6% | Lowland 16% | Flatland 46% | Highland 20% | Mountain 12%.
    public float CanyonQuantile { get; init; } = 0.06f;
    public float LowlandQuantile { get; init; } = 0.22f;
    public float FlatlandQuantile { get; init; } = 0.68f;
    public float HighlandQuantile { get; init; } = 0.88f;
    public float CraterChance { get; init; } = 0.12f; // υποσύνολο των flatlands γίνεται κρατήρες

    // --- Latitude / πολικός πάγος ---
    public float PolarLatitude { get; init; } = 0.82f; // |lat| πάνω από αυτό → πάγος

    // --- Resources (πιθανότητες ανά κατάλληλο tile) ---
    public float IceChance { get; init; } = 0.18f;
    public float IronChance { get; init; } = 0.40f;
    public float SiliconChance { get; init; } = 0.30f;
    public float RegolithChance { get; init; } = 0.30f;
    public float HiddenDepositChance { get; init; } = 0.35f;
}
