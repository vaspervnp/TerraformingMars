using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Planet;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Ακραίος καιρός της Φάσης 2B: η πυκνή ατμόσφαιρα και οι ωκεανοί τροφοδοτούν super-storms.
/// Όταν η συσσωρευμένη ενέργεια ξεπεράσει το κατώφλι, ξεσπά <b>hurricane</b> που «σαρώνει» τα ηλιακά
/// (SolarPowered) και πλημμυρίζει τα χαμηλά κτίρια — τα κάνει <see cref="BuildingState.Disabled"/>
/// (επισκευάζονται από το EventSystem). Εξαιρείται η κρίσιμη υποστήριξη ζωής (LifeSupport). Τα
/// <b>Sea Walls</b> (<see cref="BuildingDefinition.StormShieldRadius"/>) θωρακίζουν τα γύρω κτίρια.
/// </summary>
public sealed class WeatherSystem : ISimulationSystem
{
    private const double BuildupBase = 0.02;
    private const double HurricaneThreshold = 12.0;
    private const double FloodElevation = -0.2;   // κτίρια σε tiles χαμηλότερα → ευάλωτα σε πλημμύρα
    private const int RepairTicks = 250;

    public void Tick(World world)
    {
        if (!world.Phase2Active) { world.StormLevel = 0; return; }

        var planet = world.Planet;
        // Οι καταιγίδες τρέφονται από πυκνό αέρα (πίεση) + ωκεανούς (κάλυψη νερού).
        double atmos = planet.Pressure >= PlanetState.TargetPressure * 0.8 && planet.WaterCoverage >= 0.1
            ? 0.5 + planet.WaterCoverage
            : 0.0;

        double stress = world.StormStress + BuildupBase * atmos;
        if (stress >= HurricaneThreshold)
        {
            Hurricane(world);
            stress = 0;
        }

        world.StormStress = stress;
        world.StormLevel = Math.Clamp(stress / HurricaneThreshold, 0.0, 1.0);
    }

    private void Hurricane(World world)
    {
        var buildings = world.Colony.Buildings;
        var seaWalls = buildings.Where(b => b.State == BuildingState.Operational && b.Definition.StormShieldRadius > 0).ToList();

        int hit = 0;
        foreach (var b in buildings)
        {
            if (b.State != BuildingState.Operational) continue;
            if (b.Definition.Category == "LifeSupport") continue;    // κρίσιμη υποδομή — γλιτώνει

            bool wind = b.Definition.SolarPowered;                    // «σαρώνει» τα ηλιακά
            bool flood = world.Map.GetTile(b.Location) is { } t && t.Elevation < FloodElevation;
            if (!(wind || flood)) continue;

            if (seaWalls.Any(w => b.Location.DistanceTo(w.Location) <= w.Definition.StormShieldRadius)) continue;

            b.State = BuildingState.Disabled;
            b.RepairTicksRemaining = RepairTicks;
            hit++;
        }

        if (hit > 0)
        {
            world.BumpMapRevision();
            world.EventNotifications.Add($"Hurricane! {hit} building(s) battered offline");
        }
    }
}
