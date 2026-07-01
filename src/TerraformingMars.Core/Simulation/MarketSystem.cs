using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Εμπόριο με τη Γη: τα Export Terminals (<see cref="BuildingDefinition.ExportPerTick"/> &gt; 0)
/// εκτοξεύουν αποθηκευμένο <see cref="ResourceKind.Silicon"/> και το μετατρέπουν σε
/// <see cref="ResourceKind.Credits"/> (<see cref="SiliconPrice"/> ανά μονάδα, × workerEfficiency).
/// Πουλά <b>μόνο ό,τι υπάρχει ήδη στο απόθεμα</b>, οπότε ποτέ δεν «τυπώνει» credits από το τίποτα.
/// Τρέχει μετά το <see cref="ProductionSystem"/>, ώστε να πωλείται και το Silicon που εξορύχθηκε
/// σε αυτό το tick.
/// </summary>
public sealed class MarketSystem : ISimulationSystem
{
    /// <summary>Credits ανά μονάδα πουλημένου Silicon.</summary>
    public const double SiliconPrice = 35.0;

    public void Tick(World world)
    {
        var ledger = world.Colony.Ledger;

        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.Operational) continue;

            double capacity = building.Definition.ExportPerTick;
            if (capacity <= 0) continue;

            double eff = building.WorkerEfficiency();
            if (eff <= 0) continue;

            double available = ledger.Get(ResourceKind.Silicon);
            double sold = Math.Min(capacity * eff, available);
            if (sold <= 0) continue;

            ledger.Add(ResourceKind.Silicon, -sold);
            ledger.Add(ResourceKind.Credits, sold * SiliconPrice);
        }
    }
}
