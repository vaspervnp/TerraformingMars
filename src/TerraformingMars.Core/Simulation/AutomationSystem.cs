using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Αυτοματοποίηση της Φάσης 2B: τα AI Drone Hives (κτίρια με <see cref="BuildingDefinition.AutomationCapacity"/>)
/// τρέχουν βαριά βιομηχανία <b>χωρίς ανθρώπινο πλήρωμα</b>. Κάθε tick, η συνολική χωρητικότητα των hives
/// καλύπτει έως τόσα βιομηχανικά κτίρια που θα χρειάζονταν εργάτες (<see cref="BuildingState.Operational"/>,
/// Category "Industry", MaxWorkers &gt; 0), θέτοντας <see cref="Building.Automated"/> ώστε να αποδίδουν πλήρως
/// χωρίς αποίκους. Τρέχει ΝΩΡΙΣ (πριν το ProductionSystem) ώστε το flag να διαβαστεί τον ίδιο tick.
/// </summary>
public sealed class AutomationSystem : ISimulationSystem
{
    public void Tick(World world)
    {
        var buildings = world.Colony.Buildings;

        // Πάντα μηδενίζουμε πρώτα (και εκτός Φάσης 2 τα drones δεν λειτουργούν).
        foreach (var b in buildings) b.Automated = false;
        if (!world.Phase2Active) { world.AutomationLevel = 0; return; }

        int capacity = 0;
        foreach (var b in buildings)
            if (b.State == BuildingState.Operational && b.Definition.AutomationCapacity > 0)
                capacity += b.Definition.AutomationCapacity;

        int automated = 0, candidates = 0;
        foreach (var b in buildings)
        {
            if (b.State != BuildingState.Operational) continue;
            if (b.Definition.Category != "Industry" || b.Definition.MaxWorkers <= 0) continue;
            candidates++;
            if (automated < capacity) { b.Automated = true; automated++; }
        }

        world.AutomationLevel = candidates > 0 ? (double)automated / candidates : 0;
    }
}
