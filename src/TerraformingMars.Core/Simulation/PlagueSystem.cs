using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Planet;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Η Άρεια Πανώλη (Φάση 2A): ένα bio-hazard που ξεσπά όταν οι <b>ωκεανοί ξεχειλίζουν</b> πολύ πάνω
/// από τον στόχο terraforming (<see cref="PlagueWaterThreshold"/> κάλυψη νερού) — μεταλλαγμένα
/// παθογόνα ανθίζουν στα νέα νερά. Όσο εξαπλώνεται, η <see cref="World.PlagueSeverity"/> ανεβαίνει
/// και <b>στραγγαλίζει το εργατικό δυναμικό</b> (μέσω <see cref="World.PlagueEfficiency"/>, που το
/// διαβάζει το <see cref="ProductionSystem"/>). Την αναχαιτίζει η ιατρική ικανότητα: <b>Doctors</b>
/// και στελεχωμένα <b>Planetary Isolation Hospitals</b> (<see cref="BuildingDefinition.MedicalCapacity"/>).
/// Όταν οι ωκεανοί υποχωρήσουν κάτω από το κατώφλι, η πανώλη σβήνει σταδιακά μόνη της.
/// Τρέχει <b>πριν</b> το <see cref="ProductionSystem"/> ώστε ο πολλαπλασιαστής να ισχύει την ίδια φορά.
/// </summary>
public sealed class PlagueSystem : ISimulationSystem
{
    /// <summary>Κάλυψη νερού πάνω από την οποία εκκολάπτεται η πανώλη (στόχος terraforming = 0.30).</summary>
    private const double PlagueWaterThreshold = 0.40;
    private const double SpreadPerTick = 0.004;         // ρυθμός εξάπλωσης όταν είναι υγρός ο πλανήτης
    private const double NaturalDecayPerTick = 0.002;   // ύφεση όταν οι ωκεανοί υποχωρήσουν
    private const double DoctorCurePerTick = 0.004;     // ανά Doctor στην αποικία
    private const double MaxProductionDrag = 0.5;       // στη σοβαρότητα 1.0 → 50% απόδοση

    public void Tick(World world)
    {
        if (!world.Phase2Active)
        {
            world.PlagueSeverity = 0;
            world.PlagueEfficiency = 1.0;
            world.PlagueActive = false;
            return;
        }

        var colony = world.Colony;
        bool wet = world.Planet.WaterCoverage >= PlagueWaterThreshold; // σχηματισμένοι ωκεανοί → παθογόνα

        double medical = MedicalCapacity(colony);
        double spread = wet ? SpreadPerTick : 0.0;
        double decay = wet ? 0.0 : NaturalDecayPerTick;

        world.PlagueSeverity = Math.Clamp(world.PlagueSeverity + spread - medical - decay, 0.0, 1.0);
        world.PlagueActive = wet || world.PlagueSeverity > 0;
        world.PlagueEfficiency = 1.0 - world.PlagueSeverity * MaxProductionDrag;
    }

    /// <summary>Συνολική ιατρική ικανότητα/tick: στελεχωμένα Isolation Hospitals + κάθε Doctor.</summary>
    private static double MedicalCapacity(Colony colony)
    {
        double medical = 0;
        foreach (var b in colony.Buildings)
            if (b.State == BuildingState.Operational && b.Definition.MedicalCapacity > 0)
                medical += b.Definition.MedicalCapacity * b.WorkerEfficiency();

        int doctors = colony.Colonists.Count(c => c.Specialty == Specialty.Doctor);
        medical += doctors * DoctorCurePerTick;
        return medical;
    }
}
