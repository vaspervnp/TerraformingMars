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
    Silicon,  // εξορυγμένο πυρίτιο — εξαγώγιμο εμπόρευμα (πωλείται σε Credits από το MarketSystem)
    Credits,
    Research  // ροή προς την τρέχουσα έρευνα (δεν αποθηκεύεται στο ledger· το χειρίζεται το ResearchSystem)
}
