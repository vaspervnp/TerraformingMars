using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Grid;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Δίκτυο Hyperloop (Φάση 2B — The Infrastructure Boom): συνδέει τα διάσπαρτα εξορυκτικά outposts.
/// Ένα βιομηχανικό κτίριο πέρα από το <see cref="RemoteRange"/> από τον πυρήνα της αποικίας (την
/// κάψουλα προσγείωσης) θεωρείται <b>απομακρυσμένο</b>· τρέχει με μειωμένη παραγωγή
/// (<see cref="RemotePenalty"/> — logistics blackout) εκτός αν εξυπηρετείται από έναν
/// <b>Hyperloop Terminal</b> (<see cref="BuildingDefinition.HyperloopRange"/>) που είναι
/// <b>συνδεδεμένος</b> στον πυρήνα — απευθείας ή μέσω αλυσίδας άλλων terminals.
/// Αν ένας κόμβος «σπάσει» από ακραίο καιρό (<see cref="BuildingState.Disabled"/>), παύει να μεταδίδει
/// και τα εξαρτημένα outposts πέφτουν σε blackout μέχρι να επισκευαστεί (spec: extreme-weather blackout).
/// Τρέχει <b>πριν</b> το <see cref="ProductionSystem"/> ώστε ο <see cref="Building.LogisticsFactor"/>
/// να διαβαστεί την ίδια φορά· ο factor πολλαπλασιάζεται μέσα στο <see cref="Building.WorkerEfficiency"/>.
/// </summary>
public sealed class HyperloopSystem : ISimulationSystem
{
    private const int RemoteRange = 6;          // hexes από τον πυρήνα· πέρα από αυτό ⇒ απομακρυσμένο
    private const double RemotePenalty = 0.5;   // παραγωγή ασύνδετου απομακρυσμένου outpost

    private readonly List<Building> _connected = new();

    public void Tick(World world)
    {
        var buildings = world.Colony.Buildings;

        // Reset (ο factor είναι εφήμερος)· εκτός Φάσης 2 δεν υπάρχει logistics περιορισμός.
        foreach (var b in buildings) b.LogisticsFactor = 1.0;
        world.LogisticsBlackoutCount = 0;
        if (!world.Phase2Active) return;

        // Πυρήνας = κάψουλα προσγείωσης (fallback: πρώτο κτίριο). Χωρίς πυρήνα → τίποτα να αγκυρώσουμε.
        var core = buildings.FirstOrDefault(b => b.Definition.Id == "landing_capsule")
                   ?? buildings.FirstOrDefault();
        if (core is null) return;
        Hex coreHex = core.Location;

        // --- Flood-fill: ποια terminals είναι συνδεδεμένα στον πυρήνα (απευθείας ή αλυσιδωτά) ---
        _connected.Clear();
        var pending = buildings
            .Where(b => b.State == BuildingState.Operational && b.Definition.HyperloopRange > 0)
            .ToList();

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var t = pending[i];
                int range = t.Definition.HyperloopRange;
                bool linked = t.Location.DistanceTo(coreHex) <= range
                              || _connected.Any(c => t.Location.DistanceTo(c.Location) <= range);
                if (linked)
                {
                    _connected.Add(t);
                    pending.RemoveAt(i);
                    changed = true;
                }
            }
        }

        // --- Απομακρυσμένα βιομηχανικά outposts: blackout αν δεν τα εξυπηρετεί συνδεδεμένος κόμβος ---
        int blackout = 0;
        foreach (var b in buildings)
        {
            if (b.Definition.Category != "Industry") continue;                 // μόνο τα εξορυκτικά outposts
            if (b.Location.DistanceTo(coreHex) <= RemoteRange) continue;       // κοντά στον πυρήνα → εντάξει

            bool served = _connected.Any(t => b.Location.DistanceTo(t.Location) <= t.Definition.HyperloopRange);
            if (!served)
            {
                b.LogisticsFactor = RemotePenalty;
                if (b.State == BuildingState.Operational) blackout++;
            }
        }

        world.LogisticsBlackoutCount = blackout;
    }
}
