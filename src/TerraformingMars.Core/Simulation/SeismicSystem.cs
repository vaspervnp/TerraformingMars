using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Grid;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Σεισμική αστάθεια της Φάσης 2B: τα Deep Core Drills (κτίρια με <see cref="BuildingDefinition.SeismicPerTick"/>)
/// συσσωρεύουν πίεση στον φλοιό όσο λειτουργούν. Όταν ξεπεραστεί το κατώφλι, ξεσπά <b>marsquake</b> με
/// επίκεντρο ένα drill: όσα κτίρια βρίσκονται σε ακτίνα «ραγίζουν» (γίνονται <see cref="BuildingState.Disabled"/>
/// και επισκευάζονται από το EventSystem). Ο παίκτης το διαχειρίζεται απλώνοντας τα drills μακριά από
/// κρίσιμες υποδομές. Ντετερμινιστικό: το επίκεντρο είναι το πρώτο ενεργό drill.
/// </summary>
public sealed class SeismicSystem : ISimulationSystem
{
    private const double DecayPerTick = 0.01;
    private const double MarsquakeThreshold = 15.0;
    private const int MarsquakeRadius = 1;         // επίκεντρο + άμεσοι γείτονες
    private const int MarsquakeRepairTicks = 200;

    public void Tick(World world)
    {
        if (!world.Phase2Active) { world.SeismicLevel = 0; return; }

        double add = 0;
        Building? epicenter = null;
        foreach (var b in world.Colony.Buildings)
        {
            if (b.State != BuildingState.Operational || b.Definition.SeismicPerTick <= 0) continue;
            double eff = b.WorkerEfficiency();
            if (eff <= 0) continue;
            add += b.Definition.SeismicPerTick * eff;
            epicenter ??= b;   // πρώτο ενεργό drill = ντετερμινιστικό επίκεντρο
        }

        double stress = Math.Max(0, world.SeismicStress + add - DecayPerTick);

        if (stress >= MarsquakeThreshold && epicenter is not null)
        {
            Marsquake(world, epicenter.Location);
            stress = 0;
        }

        world.SeismicStress = stress;
        world.SeismicLevel = Math.Clamp(stress / MarsquakeThreshold, 0.0, 1.0);
    }

    private static void Marsquake(World world, Hex epicenter)
    {
        int cracked = 0;
        foreach (var b in world.Colony.Buildings)
        {
            if (b.State != BuildingState.Operational) continue;
            if (b.Location.DistanceTo(epicenter) > MarsquakeRadius) continue;
            b.State = BuildingState.Disabled;
            b.RepairTicksRemaining = MarsquakeRepairTicks;
            cracked++;
        }
        if (cracked > 0)
        {
            world.BumpMapRevision();
            world.EventNotifications.Add($"Marsquake! {cracked} building(s) cracked");
        }
    }
}
