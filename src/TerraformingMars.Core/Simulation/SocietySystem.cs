namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Ο κοινωνικός/πληθυσμιακός βρόχος της Φάσης 2: ο αφηρημένος πληθυσμός <see cref="Colony.Population"/>
/// καταναλώνει τροφή/νερό/ενέργεια ανά κάτοικο, μεταναστεύει προς τη διαθέσιμη στέγαση
/// (<see cref="Colony.AggregateHousing"/>) όταν υπάρχει πλεόνασμα, και <b>συρρικνώνεται</b> με
/// «systemic stagnation» όταν ξεπεράσει τη στέγαση ή λείψουν πόροι — ρίχνοντας την παραγωγή και
/// την υγεία. Στα 10.000 φτάνει η εποχή Urbanization (ξεκλειδώνει το High-Density Arcology).
/// Τρέχει τελευταίο (μετά Production/LifeSupport) ώστε να «τρώει» ό,τι απομένει.
/// </summary>
public sealed class SocietySystem : ISimulationSystem
{
    // Κατανάλωση ανά κάτοικο ανά tick (κλιμακώνεται με τον πληθυσμό — «2x food/water loops» της εποχής).
    private const double FoodPerPopPerTick = 0.0001;
    private const double WaterPerPopPerTick = 0.00007;
    private const double EnergyPerPopPerTick = 0.00006;

    private const double GrowthPerTick = 3.0;    // εισροή μεταναστών όταν υπάρχει χώρος & πλεόνασμα
    private const double DeclinePerTick = 4.0;   // απώλεια σε έλλειψη/υπερπληθυσμό (γρηγορότερη → αυτοδιόρθωση)

    private const double StagnationProductionEfficiency = 0.85; // -15% παραγωγή σε stagnation
    private const double StagnationHealthDecayPerTick = 0.001;
    private const double StagnationHealthFloor = 0.25;

    public void Tick(World world)
    {
        if (!world.Phase2Active)
        {
            world.StagnationActive = false;
            world.ProductionEfficiency = 1.0;
            return;
        }

        var colony = world.Colony;
        var ledger = colony.Ledger;
        double pop = colony.Population;
        if (pop <= 0)
        {
            world.StagnationActive = false;
            world.ProductionEfficiency = 1.0;
            return;
        }

        // Κατανάλωση ανά κάτοικο (ατομικό TryConsume: αν δεν φτάνει, δεν αφαιρεί → έλλειψη).
        bool food = ledger.TryConsume(ResourceKind.Food, pop * FoodPerPopPerTick);
        bool water = ledger.TryConsume(ResourceKind.Water, pop * WaterPerPopPerTick);
        bool energy = ledger.TryConsume(ResourceKind.Energy, pop * EnergyPerPopPerTick);
        bool shortfall = !(food && water && energy);

        double cap = colony.AggregateHousing;
        bool stagnating = shortfall || pop > cap;

        world.StagnationActive = stagnating;
        world.ProductionEfficiency = stagnating ? StagnationProductionEfficiency : 1.0;

        if (stagnating)
        {
            colony.Population = Math.Max(0, pop - DeclinePerTick);
            foreach (var c in colony.Colonists)
                c.Health = Math.Max(StagnationHealthFloor, c.Health - StagnationHealthDecayPerTick);
        }
        else if (pop < cap)
        {
            colony.Population = Math.Min(cap, pop + GrowthPerTick);
        }

        if (!world.UrbanizationReached && colony.Population >= World.UrbanizationThreshold)
        {
            world.UrbanizationReached = true;
            world.UrbanizationPending = true;
        }
    }
}
