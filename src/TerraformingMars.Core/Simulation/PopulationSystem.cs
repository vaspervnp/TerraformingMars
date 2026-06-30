using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Αύξηση πληθυσμού: νέοι άποικοι φτάνουν όταν υπάρχει διαθέσιμη στέγαση (housing) και
/// πλεόνασμα τροφής. Δίνει νόημα στις πόλεις χωρίς θόλο και λύνει την έλλειψη προσωπικού
/// για τα late-game κτίρια. Ντετερμινιστικό (seeded RNG).
/// </summary>
public sealed class PopulationSystem : ISimulationSystem
{
    private const double GrowthPerTick = 0.0025;       // ~400 ticks ανά νέο άποικο
    private const double FoodReservePerColonist = 40;  // πλεόνασμα τροφής που απαιτείται

    private static readonly Specialty[] Specialties =
        { Specialty.Geologist, Specialty.Engineer, Specialty.Botanist, Specialty.Climatologist };

    private readonly Random _rng;
    private double _growth;
    private int _born;

    public PopulationSystem(int seed) => _rng = new Random(seed * 104729 + 1);

    public void Tick(World world)
    {
        var colony = world.Colony;

        int housing = colony.Buildings
            .Where(b => b.State == BuildingState.Operational)
            .Sum(b => b.Definition.HousingCapacity);

        if (colony.Colonists.Count >= housing) return;

        double food = colony.Ledger.Get(ResourceKind.Food);
        if (food < FoodReservePerColonist * (colony.Colonists.Count + 1)) return;

        _growth += GrowthPerTick;
        if (_growth < 1.0) return;

        _growth -= 1.0;
        var specialty = Specialties[_rng.Next(Specialties.Length)];
        colony.Colonists.Add(new Colonist($"Settler #{++_born}", specialty));
        colony.Crew = colony.Colonists.Count;
    }
}
