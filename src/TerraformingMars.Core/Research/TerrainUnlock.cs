using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Research;

/// <summary>
/// «Ξεκλείδωμα εδάφους» από μια τεχνολογία: κάνει χτίσιμο σε ένα κατά τα άλλα μη-δομήσιμο
/// terrain (βουνά, νερό) εφικτό.
/// <para>
/// Αν το <see cref="RequiresDeposit"/> είναι <see cref="ResourceType.None"/>, το terrain ανοίγει
/// για κάθε κτίριο. Αλλιώς ανοίγει <b>μόνο</b> για κτίρια που εξορύσσουν αυτό το κοίτασμα
/// (π.χ. ice drill πάνω σε θάλασσα που κρύβει ακόμη πάγο) — το ίδιο το κοίτασμα ελέγχεται
/// ούτως ή άλλως από το <see cref="Buildings.BuildingDefinition.RequiresDeposit"/>.
/// </para>
/// </summary>
public sealed class TerrainUnlock
{
    public TerrainType Terrain { get; init; }

    /// <summary>None = για κάθε κτίριο· αλλιώς μόνο για ορυχεία αυτού του κοιτάσματος.</summary>
    public ResourceType RequiresDeposit { get; init; } = ResourceType.None;

    /// <summary>True αν αυτό το ξεκλείδωμα καλύπτει κτίριο που ζητά το <paramref name="buildingDeposit"/>.</summary>
    public bool Covers(TerrainType terrain, ResourceType buildingDeposit) =>
        Terrain == terrain && (RequiresDeposit == ResourceType.None || RequiresDeposit == buildingDeposit);
}
