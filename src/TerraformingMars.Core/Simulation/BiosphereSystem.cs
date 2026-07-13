using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Όταν ο πλανήτης ζεσταθεί (πάνω από το μηδέν), έχει νερό, και υπάρχει ενεργή βιόσφαιρα
/// (κυανοβακτήρια/δέντρα), η <b>βλάστηση απλώνεται</b> σε χαμηλά εδάφη. Η ατμοσφαιρική
/// οξυγόνωση γίνεται μέσω των PlanetEffects (στο <see cref="PlanetSystem"/>).
/// </summary>
public sealed class BiosphereSystem : ISimulationSystem
{
    private const double MinTemperature = 0.0;
    private const double MaxTemperature = 40.0;   // πάνω από αυτό (hot runaway) η βλάστηση δεν απλώνεται
    private const double MinWaterCoverage = 0.05;

    private List<HexTile>? _queue;
    private double _accumulator;
    private int _total;
    private int _vegetation;

    public void Tick(World world)
    {
        EnsureQueue(world);

        // authoritative κάθε tick, ώστε το μαράζωμα (Phase2System: Vegetation→Flatland) να μειώνει το Biomass.
        _vegetation = world.Map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

        double spread = 0;
        foreach (var b in world.Colony.Buildings)
        {
            if (b.State != BuildingState.Operational || b.Definition.VegetationSpreadPerTick <= 0) continue;
            if (world.IsOnStrike(b.Definition)) continue;   // απεργία Οικολόγων → σταματά η εξάπλωση βλάστησης
            spread += b.Definition.VegetationSpreadPerTick * b.WorkerEfficiency();
        }

        var planet = world.Planet;
        bool canGrow = spread > 0 && planet.Temperature >= MinTemperature
            && planet.Temperature <= MaxTemperature && planet.WaterCoverage >= MinWaterCoverage;

        if (canGrow)
        {
            _accumulator += spread;
            while (_accumulator >= 1.0 && _queue!.Count > 0)
            {
                _accumulator -= 1.0;
                var tile = _queue[^1];
                _queue.RemoveAt(_queue.Count - 1);

                // μπορεί στο μεταξύ να έγινε νερό από πλημμύρα — αν ναι, παράλειψέ το
                if (tile.Terrain is TerrainType.Flatland or TerrainType.Lowland)
                {
                    tile.Terrain = TerrainType.Vegetation;
                    _vegetation++;
                    world.BumpMapRevision();
                }
            }
        }

        if (_total > 0) planet.SetBiomass((double)_vegetation / _total);
    }

    private void EnsureQueue(World world)
    {
        // Ξαναχτίζεται και όταν αδειάσει, ώστε tiles που μαράθηκαν από runaway (Vegetation→Flatland)
        // να ξαναμπαίνουν στην ουρά και να αναγεννώνται μόλις το κλίμα επιστρέψει στο habitable band.
        if (_queue is { Count: > 0 }) return;

        _total = world.Map.Count;
        _vegetation = world.Map.Tiles.Count(t => t.Terrain == TerrainType.Vegetation);

        // χαμηλότερο υψόμετρο στο τέλος (pop πρώτα κοντά στο νερό)
        _queue = world.Map.Tiles
            .Where(t => t.Terrain is TerrainType.Flatland or TerrainType.Lowland)
            .OrderByDescending(t => t.Elevation)
            .ToList();
    }
}
