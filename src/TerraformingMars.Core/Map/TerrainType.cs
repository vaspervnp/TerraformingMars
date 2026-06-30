namespace TerraformingMars.Core.Map;

/// <summary>
/// Τύπος εδάφους ενός hex. Καθορίζει buildability, yield modifiers και εμφάνιση.
/// Το <see cref="Water"/> δεν παράγεται αρχικά — εμφανίζεται στη Φάση 5 όταν λιώνει ο πάγος.
/// </summary>
public enum TerrainType
{
    Canyon,
    Lowland,
    Flatland,
    Crater,
    Highland,
    Mountain,
    PolarIce,
    Water
}
