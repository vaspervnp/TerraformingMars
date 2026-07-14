using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Οικοσύστημα & εισβλητικά είδη (Φάση 2B): καθώς η βιόσφαιρα ανθίζει, φορτία από τη Γη φέρνουν
/// παράσιτα. Η πίεση εισβολής κλιμακώνεται με το <see cref="Planet.PlanetState.Biomass"/>· την
/// αντισταθμίζει η βιοποικιλότητα — <b>Wildlife Reserves / Genetic Vaults</b> (κτίρια με
/// <see cref="BuildingDefinition.EcosystemStability"/>) και η έγκριση των Οικολόγων. Όταν η μόλυνση
/// ξεπεράσει το κατώφλι, τα παράσιτα <b>τρώνε σοδειές</b> (Food) και <b>μαραίνουν βλάστηση</b>
/// (μειώνοντας Biomass/οξυγόνο). Το <see cref="World.InfestationLevel"/> τροφοδοτεί το HUD.
/// </summary>
public sealed class EcosystemSystem : ISimulationSystem
{
    private const double InvasionBaseRate = 0.0012;      // πίεση/tick (× βιομάζα)
    private const double EcologistSuppression = 0.0012;  // × έγκριση Οικολόγων
    private const double DrainThreshold = 0.30;          // πάνω από αυτό ξεκινά η ζημιά
    private const double FoodDrainPerTick = 0.4;         // × μόλυνση
    private const double WitherTilesPerTick = 3.0;       // ceil(μόλυνση × αυτό): 1 tile (~0.30) → 3 tiles (1.0)

    public void Tick(World world)
    {
        if (!world.Phase2Active) { world.InfestationLevel = 0; return; }

        var colony = world.Colony;

        double suppression = EcologistSuppression * colony.EcologistApproval;
        foreach (var b in colony.Buildings)
            if (b.State == BuildingState.Operational && b.Definition.EcosystemStability > 0)
                suppression += b.Definition.EcosystemStability;

        double pressure = InvasionBaseRate * (0.5 + world.Planet.Biomass); // περισσότερη ζωή → περισσότερα παράσιτα
        world.InfestationLevel = Math.Clamp(world.InfestationLevel + pressure - suppression, 0.0, 1.0);

        if (world.InfestationLevel > DrainThreshold)
        {
            colony.Ledger.TryConsume(ResourceKind.Food, world.InfestationLevel * FoodDrainPerTick);
            WitherVegetation(world, (int)Math.Ceiling(world.InfestationLevel * WitherTilesPerTick));
        }
    }

    /// <summary>Μαραίνει τα <paramref name="count"/> tiles υψηλότερου υψομέτρου βλάστησης (→ Flatland).
    /// Αναγεννώνται όταν υποχωρήσει η μόλυνση (η growth queue της βιόσφαιρας ξαναχτίζεται).</summary>
    private static void WitherVegetation(World world, int count)
    {
        if (count <= 0) return;
        var targets = world.Map.Tiles
            .Where(t => t.Terrain == TerrainType.Vegetation)
            .OrderByDescending(t => t.Elevation)
            .Take(count)
            .ToList();
        foreach (var t in targets) t.Terrain = TerrainType.Flatland;
        if (targets.Count > 0) world.BumpMapRevision();
    }
}
