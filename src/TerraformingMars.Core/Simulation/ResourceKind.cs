namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Επεξεργασμένοι πόροι της αποικίας (διαφορετικοί από τα ακατέργαστα ορυκτά του χάρτη
/// — Ice/Iron/Silicon/Regolith — που εξορύσσονται και μετατρέπονται σε αυτούς).
/// <see cref="Energy"/> = αποθηκευμένη ενέργεια σε μπαταρίες.
/// </summary>
public enum ResourceKind
{
    Energy,
    Water,
    Oxygen,
    Food,
    Materials,
    Credits
}
