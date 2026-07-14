using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Η Άρεια Πανώλη (Φάση 2A): ένα bio-hazard ενός <b>υδάτινου πλέον πλανήτη</b> — μόλις σχηματιστούν
/// οι ωκεανοί (<see cref="PlagueWaterThreshold"/> κάλυψη νερού, ουσιαστικά όποτε ολοκληρωθεί το
/// terraforming του νερού), μεταλλαγμένα παθογόνα ανθίζουν. Μετά από μια περίοδο χάριτος
/// (<see cref="GraceTicks"/> — χρόνος για έρευνα Macro-Epidemiology & χτίσιμο νοσοκομείου), η
/// <see cref="World.PlagueSeverity"/> ανεβαίνει σιγά-σιγά και <b>στραγγαλίζει το εργατικό δυναμικό</b>
/// (μέσω <see cref="World.PlagueEfficiency"/>, που το διαβάζει το <see cref="ProductionSystem"/>).
/// Μοναδική θεραπεία: <b>στελεχωμένα Planetary Isolation Hospitals</b>
/// (<see cref="BuildingDefinition.MedicalCapacity"/> × worker efficiency — οι <b>Doctors</b> τα ενισχύουν
/// κατά 1.5×). Αν οι ωκεανοί υποχωρήσουν κάτω από το κατώφλι, η πανώλη σβήνει σταδιακά μόνη της.
/// Τρέχει <b>πριν</b> το <see cref="ProductionSystem"/> ώστε ο πολλαπλασιαστής να ισχύει την ίδια φορά.
/// </summary>
public sealed class PlagueSystem : ISimulationSystem
{
    /// <summary>Κάλυψη νερού πάνω από την οποία εκκολάπτεται η πανώλη. Κάτω από τον στόχο terraforming
    /// (0.30) ώστε ένας πλήρως υδάτινος πλανήτης να είναι αξιόπιστα «υγρός» — η πανώλη είναι μόνιμος
    /// κίνδυνος της Φάσης 2, όχι μια σχεδόν ανέφικτη υπερχείλιση.</summary>
    private const double PlagueWaterThreshold = 0.25;
    private const double SpreadPerTick = 0.004;         // αργός ρυθμός εξάπλωσης (~250 ticks για 50% drag)
    private const double NaturalDecayPerTick = 0.002;   // ύφεση όταν οι ωκεανοί υποχωρήσουν

    /// <summary>Περίοδος χάριτος (ticks) στην αρχή της Φάσης 2 πριν αρχίσει η εξάπλωση — δίνει χρόνο να
    /// ερευνηθεί το Macro-Epidemiology και να χτιστεί/στελεχωθεί ένα Isolation Hospital.</summary>
    private const long GraceTicks = (long)(GameClock.TicksPerSol * 3); // ~3 Sols
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
        // «Υγρός» + πέρα από το grace → τα παθογόνα εξαπλώνονται· αλλιώς η μόνη κίνηση είναι θεραπεία/ύφεση.
        bool spreading = world.Planet.WaterCoverage >= PlagueWaterThreshold && world.Phase2Ticks >= GraceTicks;

        double medical = MedicalCapacity(colony);
        double spread = spreading ? SpreadPerTick : 0.0;
        double decay = spreading ? 0.0 : NaturalDecayPerTick;

        world.PlagueSeverity = Math.Clamp(world.PlagueSeverity + spread - medical - decay, 0.0, 1.0);
        world.PlagueActive = world.PlagueSeverity > 0;
        world.PlagueEfficiency = 1.0 - world.PlagueSeverity * MaxProductionDrag;
    }

    /// <summary>Ιατρική ικανότητα/tick: αποκλειστικά από <b>στελεχωμένα</b> Isolation Hospitals (η θεραπεία
    /// κλιμακώνεται με το worker efficiency — υγεία & στελέχωση· Doctors → 1.5×). Ένας μεμονωμένος γιατρός
    /// χωρίς νοσοκομείο δεν αρκεί για ένα πλανητικό παθογόνο, ώστε το κτίριο να έχει πραγματικό ρόλο.</summary>
    private static double MedicalCapacity(Colony colony)
    {
        double medical = 0;
        foreach (var b in colony.Buildings)
            if (b.State == BuildingState.Operational && b.Definition.MedicalCapacity > 0)
                medical += b.Definition.MedicalCapacity * b.WorkerEfficiency();
        return medical;
    }
}
