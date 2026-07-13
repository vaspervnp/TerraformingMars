namespace TerraformingMars.Core.Research;

/// <summary>
/// Στατικός ορισμός τεχνολογίας στο δέντρο έρευνας (data-driven, από JSON).
/// <see cref="Phase"/> 1–4 αντιστοιχεί στις 4 φάσεις των προδιαγραφών
/// (Επιβίωση / Βιομηχανία / Macro-Engineering / Βιόσφαιρα).
/// </summary>
public sealed class TechDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Phase { get; init; } = 1;

    /// <summary>Αν true, η τεχνολογία ξεκλειδώνει μόνο αφού ξεκινήσει η Φάση 2 (ολοκλήρωση terraforming).</summary>
    public bool RequiresPhase2 { get; init; } = false;

    /// <summary>Research points που χρειάζονται.</summary>
    public double Cost { get; init; } = 100;

    /// <summary>Tech ids που πρέπει να έχουν ερευνηθεί πρώτα.</summary>
    public List<string> Prerequisites { get; init; } = new();

    /// <summary>Building ids που ξεκλειδώνει.</summary>
    public List<string> Unlocks { get; init; } = new();
}
