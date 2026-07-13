using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Ρύπανση της Φάσης 2: η βαριά βιομηχανία (κτίρια με <see cref="BuildingDefinition.PollutionPerTick"/>)
/// συσσωρεύει τοπική ρύπανση στο hex της. Όταν ένα hex ξεπεράσει το κατώφλι, μαραίνει γειτονική
/// <b>βλάστηση</b> (Vegetation→Flatland). Η ρύπανση διαχέεται αργά μόνη της, και οι μονάδες
/// αντιρρύπανσης (<see cref="BuildingDefinition.ScrubsPollution"/>) την καθαρίζουν στη γειτονιά τους.
/// Ο συνολικός δείκτης <see cref="World.PollutionLevel"/> θυμώνει τους Οικολόγους (FactionSystem).
/// </summary>
public sealed class PollutionSystem : ISimulationSystem
{
    private const double DecayPerTick = 0.004;       // φυσική διάχυση
    private const double WitherThreshold = 1.5;      // πάνω από αυτό μαραίνεται γειτονική βλάστηση
    private const double ScrubPerTick = 0.05;        // καθαρισμός ανά scrubber σε hex + γείτονες
    private const double PollutionLevelScale = 60.0; // κανονικοποίηση του συνολικού δείκτη σε ~0..1

    public void Tick(World world)
    {
        if (!world.Phase2Active) { world.PollutionLevel = 0; return; }

        var map = world.Map;

        // 1. Εκπομπές από τη βιομηχανία (όχι σε απεργία), 2. καθαρισμός από scrubbers.
        foreach (var b in world.Colony.Buildings)
        {
            if (b.State != BuildingState.Operational) continue;
            var def = b.Definition;

            if (def.PollutionPerTick > 0 && !world.IsOnStrike(def))
            {
                double eff = b.WorkerEfficiency();
                if (eff > 0 && map.GetTile(b.Location) is { } t)
                    t.Pollution += def.PollutionPerTick * eff;
            }

            if (def.ScrubsPollution)
                Scrub(map, b.Location);
        }

        // 3. Διάχυση + μαράζωμα + συνολικός δείκτης (ένα πέρασμα).
        double total = 0;
        foreach (var tile in map.Tiles)
        {
            if (tile.Pollution <= 0) continue;
            tile.Pollution = Math.Max(0, tile.Pollution - DecayPerTick);
            if (tile.Pollution > WitherThreshold) WitherAdjacentVegetation(world, tile.Coord);
            total += tile.Pollution;
        }

        world.PollutionLevel = Math.Clamp(total / PollutionLevelScale, 0.0, 1.0);
    }

    private static void Scrub(HexMap map, Hex center)
    {
        Reduce(map.GetTile(center));
        for (int side = 0; side < 6; side++) Reduce(map.GetTile(center.Neighbor(side)));

        static void Reduce(HexTile? t) { if (t is not null) t.Pollution = Math.Max(0, t.Pollution - ScrubPerTick); }
    }

    /// <summary>Μαραίνει ΕΝΑ γειτονικό tile βλάστησης (ντετερμινιστικά, το πρώτο) σε Flatland.</summary>
    private static void WitherAdjacentVegetation(World world, Hex center)
    {
        for (int side = 0; side < 6; side++)
        {
            var t = world.Map.GetTile(center.Neighbor(side));
            if (t is { Terrain: TerrainType.Vegetation })
            {
                t.Terrain = TerrainType.Flatland;
                world.BumpMapRevision();
                return;
            }
        }
    }
}
