using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Προωθεί την κατασκευή των κτιρίων που χτίζονται. Παρουσία <see cref="Specialty.Engineer"/>
/// στους workers επιταχύνει την πρόοδο. Όταν ολοκληρωθεί → operational.
/// </summary>
public sealed class ConstructionSystem : ISimulationSystem
{
    public void Tick(World world)
    {
        var ledger = world.Colony.Ledger;

        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.UnderConstruction) continue;

            var def = building.Definition;
            int speed = building.Workers.Any(w => w.Specialty == Specialty.Engineer) ? 2 : 1; // Engineer επιταχύνει

            // Σταδιακή παράδοση υλικών· χωρίς αρκετά → η κατασκευή σταματά (stall).
            double totalMaterials = def.Cost.GetValueOrDefault(ResourceKind.Materials);
            if (totalMaterials > 0 && def.BuildTimeTicks > 0)
            {
                double needThisTick = totalMaterials / def.BuildTimeTicks * speed;
                if (!ledger.TryConsume(ResourceKind.Materials, needThisTick))
                {
                    building.Stalled = true;
                    continue;
                }
                building.MaterialsPaid += needThisTick;
            }

            building.Stalled = false;
            building.BuildProgress += speed;

            if (building.BuildProgress >= def.BuildTimeTicks)
                world.Colony.MarkOperational(building);
        }
    }
}

/// <summary>
/// Εφαρμόζει την καθαρή παραγωγή/κατανάλωση όλων των operational κτιρίων, πολλαπλασιασμένη
/// με το <see cref="Building.WorkerEfficiency"/> (στελέχωση + bonus ειδικότητας).
/// </summary>
public sealed class ProductionSystem : ISimulationSystem
{
    private readonly Dictionary<ResourceKind, double> _net = new();

    public void Tick(World world)
    {
        world.PowerOutage = world.Colony.Ledger.Get(ResourceKind.Energy) <= 0.0001;

        _net.Clear();
        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.Operational) continue;
            double eff = building.WorkerEfficiency();
            if (eff <= 0) continue;

            var def = building.Definition;
            double factor = eff;

            // Ορυχεία: η παραγωγή περιορίζεται από / εξαντλεί το κοίτασμα του hex.
            if (def.ExtractionPerTick > 0)
            {
                var tile = world.Map.GetTile(building.Location);
                double need = def.ExtractionPerTick * eff;
                double extracted = tile?.Extract(need) ?? 0;
                building.DepositDepleted = tile is null || tile.RemainingDeposit <= 0;
                if (extracted <= 0) continue; // εξαντλημένο κοίτασμα → αδρανές
                factor = extracted / def.ExtractionPerTick;
            }

            if (def.SolarPowered) factor *= world.SolarEfficiency; // αμμοθύελλα μειώνει τα ηλιακά

            foreach (var (kind, perTick) in def.Production)
            {
                if (kind == ResourceKind.Research) continue; // το χειρίζεται το ResearchSystem
                _net[kind] = _net.GetValueOrDefault(kind) + perTick * factor;
            }
        }

        var ledger = world.Colony.Ledger;
        foreach (var (kind, perTick) in _net)
            ledger.Add(kind, perTick);
    }
}

/// <summary>
/// Συγκεντρώνει research points από τα operational εργαστήρια (Production[Research] × efficiency)
/// και τα προσθέτει στο τρέχον target του δέντρου τεχνολογίας.
/// </summary>
public sealed class ResearchSystem : ISimulationSystem
{
    public void Tick(World world)
    {
        var tech = world.Colony.Tech;
        if (tech.CurrentTarget is null) return;

        double points = 0;
        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.Operational) continue;
            double output = building.Definition.Production.GetValueOrDefault(ResourceKind.Research);
            if (output > 0) points += output * building.WorkerEfficiency();
        }

        tech.AddProgress(points);
    }
}

/// <summary>
/// Καταναλώνει O₂/νερό/τροφή ανάλογα με το πλήρωμα. Αν λείψει οτιδήποτε,
/// σηκώνει <see cref="Colony.LifeSupportFailing"/>.
/// </summary>
public sealed class LifeSupportSystem : ISimulationSystem
{
    public double OxygenPerCrew { get; init; } = 0.08;
    public double WaterPerCrew { get; init; } = 0.05;
    public double FoodPerCrew { get; init; } = 0.03;

    public void Tick(World world)
    {
        var colony = world.Colony;
        var ledger = colony.Ledger;
        int crew = colony.Crew;

        bool oxygen = ledger.TryConsume(ResourceKind.Oxygen, OxygenPerCrew * crew);
        bool water = ledger.TryConsume(ResourceKind.Water, WaterPerCrew * crew);
        bool food = ledger.TryConsume(ResourceKind.Food, FoodPerCrew * crew);

        colony.LifeSupportFailing = crew > 0 && !(oxygen && water && food);
    }
}
