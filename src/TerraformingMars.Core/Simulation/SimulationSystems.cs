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
/// Εφαρμόζει παραγωγή/κατανάλωση των operational κτιρίων (× efficiency, mining, ηλιακά).
/// <b>Brownout gating</b>: αν η διαθέσιμη ενέργεια (αποθηκευμένη + παραγωγή) δεν καλύπτει την
/// κατανάλωση, οι καταναλωτές στραγγαλίζονται αναλογικά (π.χ. σε αμμοθύελλα χωρίς μπαταρίες/fission).
/// </summary>
public sealed class ProductionSystem : ISimulationSystem
{
    private readonly Dictionary<ResourceKind, double> _net = new();
    private readonly List<(Building building, double factor)> _active = new();

    public void Tick(World world)
    {
        var ledger = world.Colony.Ledger;

        // --- Pass 1: factor ανά κτίριο + ισοζύγιο ενέργειας ---
        _active.Clear();
        double energyProduction = 0, energyConsumption = 0;

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

            _active.Add((building, factor));

            double energy = def.Production.GetValueOrDefault(ResourceKind.Energy) * factor;
            if (energy >= 0) energyProduction += energy;
            else energyConsumption += -energy;
        }

        double available = ledger.Get(ResourceKind.Energy) + energyProduction;
        double brownout = energyConsumption > available && energyConsumption > 0
            ? available / energyConsumption
            : 1.0;
        world.PowerOutage = brownout < 0.999;

        // --- Pass 2: εφαρμογή (οι καταναλωτές × brownout) ---
        _net.Clear();
        foreach (var (building, factor) in _active)
        {
            var def = building.Definition;
            double energy = def.Production.GetValueOrDefault(ResourceKind.Energy) * factor;
            double scale = energy < 0 ? brownout : 1.0;

            foreach (var (kind, perTick) in def.Production)
            {
                if (kind == ResourceKind.Research) continue; // το χειρίζεται το ResearchSystem
                _net[kind] = _net.GetValueOrDefault(kind) + perTick * factor * scale;
            }
        }

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

    private const int GraceTicks = 200;              // περιθώριο πριν αρχίσουν θάνατοι (~λίγα λεπτά)
    private const double DeathRatePerTick = 0.02;    // ~1 θάνατος ανά 50 ticks μετά το grace

    private double _deathAccumulator;

    public void Tick(World world)
    {
        var colony = world.Colony;
        var ledger = colony.Ledger;
        int crew = colony.Crew;

        bool oxygen = ledger.TryConsume(ResourceKind.Oxygen, OxygenPerCrew * crew);
        bool water = ledger.TryConsume(ResourceKind.Water, WaterPerCrew * crew);
        bool food = ledger.TryConsume(ResourceKind.Food, FoodPerCrew * crew);

        colony.LifeSupportFailing = crew > 0 && !(oxygen && water && food);

        if (!colony.LifeSupportFailing)
        {
            colony.LifeSupportFailingTicks = 0;
            _deathAccumulator = 0;
            return;
        }

        // Παρατεταμένη αποτυχία → οι άποικοι αρχίζουν να πεθαίνουν.
        colony.LifeSupportFailingTicks++;
        if (colony.LifeSupportFailingTicks <= GraceTicks) return;

        _deathAccumulator += DeathRatePerTick;
        while (_deathAccumulator >= 1.0 && colony.Colonists.Count > 0)
        {
            _deathAccumulator -= 1.0;
            var victim = colony.Colonists[^1];
            colony.Unassign(victim);
            colony.Colonists.RemoveAt(colony.Colonists.Count - 1);
            colony.Crew = colony.Colonists.Count;
        }

        if (colony.Colonists.Count == 0) colony.Collapsed = true;
    }
}
