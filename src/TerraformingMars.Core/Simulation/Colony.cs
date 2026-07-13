using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Research;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Η ανθρώπινη βάση: αποθέματα, πλήρωμα, κτίρια. Διαχειρίζεται τοποθέτηση κτιρίων
/// (με validation & κόστος) και ανάθεση αποίκων σε θέσεις εργασίας.
/// (Ο χάρτης ζει στον <see cref="World"/>.)
/// </summary>
public sealed class Colony
{
    public ResourceLedger Ledger { get; } = new();
    public TechTree Tech { get; } = new();
    public int Crew { get; set; }

    /// <summary>Συνολικός (αφηρημένος) μεταναστευτικός πληθυσμός της Φάσης 2 — ξεχωριστός από τους
    /// επώνυμους <see cref="Colonists"/>/<see cref="Crew"/>. double ώστε να συσσωρεύεται κλασματικά.</summary>
    public double Population { get; set; }

    public List<Building> Buildings { get; } = new();
    public List<Colonist> Colonists { get; } = new();

    /// <summary>Βασική στέγαση (από τον χορηγό/δυσκολία) — προστίθεται στη χωρητικότητα των habitat κτιρίων.</summary>
    public int BaseHousing { get; set; }

    /// <summary>Συνολικό όριο πληθυσμού: βάση χορηγού + HousingCapacity όλων των operational κτιρίων.</summary>
    public int Housing => BaseHousing + Buildings
        .Where(b => b.State == BuildingState.Operational)
        .Sum(b => b.Definition.HousingCapacity);

    public bool LifeSupportFailing { get; set; }
    public int LifeSupportFailingTicks { get; internal set; }
    public bool Collapsed { get; internal set; }

    public IEnumerable<Colonist> IdleColonists => Colonists.Where(c => c.Assignment is null);

    public bool IsOccupied(Hex hex) => Buildings.Any(b => b.Location == hex);

    /// <summary>True αν κάποιο από τα 6 γειτονικά hex έχει κτίριο-κατοικία (Category "Habitat"),
    /// π.χ. την κάψουλα προσγείωσης ή άλλο habitat module — για «κούμπωμα» νέων κατοικιών στο δίκτυο.</summary>
    public bool HasAdjacentHabitat(Hex hex)
    {
        for (int side = 0; side < 6; side++)
        {
            Hex n = hex.Neighbor(side);
            if (Buildings.Any(b => b.Location == n && b.Definition.Category == "Habitat"))
                return true;
        }
        return false;
    }

    /// <summary>Προσθέτει έτοιμο instance κτιρίου· αν είναι ήδη operational, εφαρμόζει την αποθήκευσή του.</summary>
    public void AddBuilding(Building building)
    {
        Buildings.Add(building);
        if (building.State == BuildingState.Operational)
            ApplyStorage(building);
    }

    /// <summary>Καλείται από το ConstructionSystem όταν ολοκληρωθεί η κατασκευή.</summary>
    internal void MarkOperational(Building building)
    {
        if (building.State == BuildingState.Operational) return;
        building.State = BuildingState.Operational;
        ApplyStorage(building);
    }

    private void ApplyStorage(Building building)
    {
        foreach (var (kind, cap) in building.Definition.Storage)
            Ledger.AddCapacity(kind, cap);
    }

    /// <summary>Ελέγχει αν επιτρέπεται η τοποθέτηση χωρίς παρενέργειες (για ghost preview & validation).</summary>
    public PlacementResult CanPlace(BuildingDefinition def, Hex hex, HexMap map)
    {
        if (!def.Buildable) return PlacementResult.Fail("not buildable");
        if (!Tech.IsResearched(def.RequiredTech)) return PlacementResult.Fail("locked (needs research)");

        var tile = map.GetTile(hex);
        if (tile is null) return PlacementResult.Fail("outside map");
        if (!tile.IsBuildable) return PlacementResult.Fail($"cannot build on {tile.Terrain}");
        if (def.AllowedTerrain.Count > 0 && !def.AllowedTerrain.Contains(tile.Terrain))
            return PlacementResult.Fail($"needs {string.Join("/", def.AllowedTerrain)}");
        if (def.RequiresDeposit != ResourceType.None && tile.Deposit.Type != def.RequiresDeposit)
            return PlacementResult.Fail($"needs {def.RequiresDeposit} deposit");
        if (def.RequiresHabitatLink && !HasAdjacentHabitat(hex))
            return PlacementResult.Fail("must connect to a habitat");
        if (IsOccupied(hex)) return PlacementResult.Fail("occupied");

        // Τα Materials καταναλώνονται σταδιακά κατά την κατασκευή (όχι upfront).
        // Τα υπόλοιπα (Credits) πληρώνονται μπροστά ως «παραγγελία».
        foreach (var (kind, amount) in def.Cost)
            if (kind != ResourceKind.Materials && Ledger.Get(kind) < amount)
                return PlacementResult.Fail($"needs {amount:0} {kind}");

        return PlacementResult.Ok(null!);
    }

    /// <summary>Τοποθετεί κτίριο: πληρώνει το upfront κόστος (Credits) και ξεκινά κατασκευή.
    /// Το <paramref name="currentTick"/> καταγράφεται για τον χρονικά-φθίνοντα υπολογισμό reclaim.</summary>
    public PlacementResult TryPlaceBuilding(BuildingDefinition def, Hex hex, HexMap map, long currentTick = 0)
    {
        var check = CanPlace(def, hex, map);
        if (!check.Success) return check;

        foreach (var (kind, amount) in def.Cost)
            if (kind != ResourceKind.Materials)
                Ledger.TryConsume(kind, amount);

        var building = new Building(def, hex) { CreatedTick = currentTick };
        AddBuilding(building);
        return PlacementResult.Ok(building);
    }

    /// <summary>
    /// Ποσοστό επιστροφής credits για ανακύκλωση κτιρίου: ξεκινά στο 90% και χάνει 2%
    /// ανά Sol που έχει περάσει από την τοποθέτηση, με κατώφλι 20%.
    /// </summary>
    public static double ReclaimFraction(Building building, long currentTick)
    {
        double sols = Math.Max(0, currentTick - building.CreatedTick) / GameClock.TicksPerSol;
        return Math.Clamp(0.90 - 0.02 * sols, 0.20, 0.90);
    }

    /// <summary>Credits που θα επιστραφούν αν ανακυκλωθεί τώρα το κτίριο (ποσοστό × κόστος σε Credits).</summary>
    public double ReclaimValue(Building building, long currentTick)
    {
        double creditCost = building.Definition.Cost.GetValueOrDefault(ResourceKind.Credits);
        return creditCost * ReclaimFraction(building, currentTick);
    }

    /// <summary>
    /// Materials (regolith) που θα επιστραφούν αν ανακυκλωθεί τώρα το κτίριο: το ίδιο χρονικά-φθίνον
    /// ποσοστό, επί των υλικών που έχουν <b>πραγματικά</b> επενδυθεί στο εργοτάξιο (<see cref="Building.MaterialsPaid"/>).
    /// Ένα ημιτελές κτίριο έχει πληρώσει λιγότερα υλικά, οπότε επιστρέφει λιγότερα (κανένα «τύπωμα» υλικών).
    /// </summary>
    public double ReclaimMaterialsValue(Building building, long currentTick)
        => building.MaterialsPaid * ReclaimFraction(building, currentTick);

    /// <summary>True αν το κτίριο μπορεί να ανακυκλωθεί (buildable — π.χ. όχι η κάψουλα προσεδάφισης).</summary>
    public static bool CanReclaim(Building building) => building.Definition.Buildable;

    /// <summary>
    /// Ανακυκλώνει κτίριο: επιστρέφει το χρονικά-φθίνον ποσοστό τόσο των Credits όσο και των
    /// Materials (regolith) που επενδύθηκαν, αποδεσμεύει το προσωπικό, αφαιρεί τη χωρητικότητα
    /// αποθήκευσης και το κτίριο από τον χάρτη.
    /// Επιστρέφει τα (Credits, Materials) που δόθηκαν, ή (0, 0) αν δεν ήταν δυνατή η ανακύκλωση.
    /// </summary>
    public (double Credits, double Materials) Reclaim(Building building, long currentTick)
    {
        if (!CanReclaim(building) || !Buildings.Contains(building)) return (0.0, 0.0);

        foreach (var worker in building.Workers.ToList())
            Unassign(worker);

        // Αφαίρεση χωρητικότητας που είχε προσθέσει το κτίριο (μόνο αν λειτουργούσε).
        if (building.State == BuildingState.Operational)
            foreach (var (kind, cap) in building.Definition.Storage)
            {
                Ledger.AddCapacity(kind, -cap);
                Ledger.Set(kind, Ledger.Get(kind)); // re-clamp στη μειωμένη χωρητικότητα
            }

        double creditRefund = ReclaimValue(building, currentTick);
        double materialRefund = ReclaimMaterialsValue(building, currentTick);
        Ledger.Add(ResourceKind.Credits, creditRefund);
        Ledger.Add(ResourceKind.Materials, materialRefund);
        Buildings.Remove(building);
        return (creditRefund, materialRefund);
    }

    /// <summary>Αναθέτει άποικο σε κτίριο (αν υπάρχει ελεύθερη θέση). Τον αφαιρεί από προηγούμενη θέση.</summary>
    public bool Assign(Colonist colonist, Building building)
    {
        if (building.Definition.MaxWorkers <= 0) return false;
        if (building.Workers.Count >= building.Definition.MaxWorkers) return false;

        colonist.Assignment?.Workers.Remove(colonist);
        building.Workers.Add(colonist);
        colonist.Assignment = building;
        return true;
    }

    /// <summary>Αφαιρεί άποικο από τη θέση εργασίας του (γίνεται idle).</summary>
    public bool Unassign(Colonist colonist)
    {
        if (colonist.Assignment is null) return false;
        colonist.Assignment.Workers.Remove(colonist);
        colonist.Assignment = null;
        return true;
    }
}
