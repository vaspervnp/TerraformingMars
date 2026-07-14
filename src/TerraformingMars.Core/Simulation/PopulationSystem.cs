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

    // Στη Φάση 2 φτάνουν και Doctors (χρειάζονται για την Άρεια Πανώλη / τα Isolation Hospitals).
    private static readonly Specialty[] Phase2Specialties =
        { Specialty.Geologist, Specialty.Engineer, Specialty.Botanist, Specialty.Climatologist, Specialty.Doctor };

    private readonly Random _rng;
    private double _growth;
    private int _born;

    public PopulationSystem(int seed) => _rng = new Random(seed * 104729 + 1);

    public void Tick(World world)
    {
        var colony = world.Colony;

        if (colony.Colonists.Count >= colony.Housing) return;

        double food = colony.Ledger.Get(ResourceKind.Food);
        if (food < FoodReservePerColonist * (colony.Colonists.Count + 1)) return;

        _growth += GrowthPerTick;
        if (_growth < 1.0) return;

        _growth -= 1.0;
        var pool = world.Phase2Active ? Phase2Specialties : Specialties;
        var specialty = pool[_rng.Next(pool.Length)];
        colony.Colonists.Add(new Colonist($"Settler #{++_born}", specialty));
        colony.Crew = colony.Colonists.Count;
    }
}
