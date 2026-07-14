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
    /// <summary>Credits ανά μονάδα πουλημένου Silicon (παγκόσμια τιμή).</summary>
    public const double SiliconPrice = 35.0;
    /// <summary>Credits ανά μονάδα πλεονάζοντος Materials (Interplanetary Stock Exchange).</summary>
    public const double MaterialsPrice = 20.0;
    /// <summary>Απόθεμα Materials που δεν πωλείται (για κατασκευές).</summary>
    public const double MaterialsReserve = 300.0;

    public void Tick(World world)
    {
        var ledger = world.Colony.Ledger;

        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.Operational) continue;
            if (world.IsOnStrike(building.Definition)) continue;

            double eff = building.WorkerEfficiency();
            if (eff <= 0) continue;

            var def = building.Definition;

            // Πώληση Silicon (export terminal / quantum plant με 10× τιμή) — μόνο ό,τι υπάρχει.
            if (def.ExportPerTick > 0)
            {
                double sold = Math.Min(def.ExportPerTick * eff, ledger.Get(ResourceKind.Silicon));
                if (sold > 0)
                {
                    double price = def.SiliconExportPrice > 0 ? def.SiliconExportPrice : SiliconPrice;
                    ledger.Add(ResourceKind.Silicon, -sold);
                    ledger.Add(ResourceKind.Credits, sold * price);
                }
            }

            // Πώληση πλεονάζοντος Materials (stock exchange) — πάνω από το reserve.
            if (def.MaterialsExportPerTick > 0)
            {
                double surplus = ledger.Get(ResourceKind.Materials) - MaterialsReserve;
                double sold = Math.Min(def.MaterialsExportPerTick * eff, Math.Max(0, surplus));
                if (sold > 0)
                {
                    ledger.Add(ResourceKind.Materials, -sold);
                    ledger.Add(ResourceKind.Credits, sold * MaterialsPrice);
                }
            }
        }
    }
}
