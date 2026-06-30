using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Οδηγεί το terraforming: εφαρμόζει τις πλανητικές επιδράσεις των macro-engineering κτιρίων
/// (× efficiency), την ατμοσφαιρική απώλεια χωρίς μαγνητόσφαιρα, και τη <b>μετατροπή tiles</b>
/// (πάγος→νερό από θερμοκρασία, πλημμύρα χαμηλών εδαφών από κομήτες). Η κάλυψη νερού
/// προκύπτει από τα tiles νερού του χάρτη.
/// </summary>
public sealed class PlanetSystem : ISimulationSystem
{
    private const double MeltStartTemperature = -15.0;     // πάνω από αυτό αρχίζει το λιώσιμο
    private const double MeltTilesPerDegreePerTick = 0.04;  // ρυθμός λιωσίματος ανά °C πάνω από το κατώφλι
    private const double PressureLeakPerTick = 0.004;       // απώλεια χωρίς μαγνητόσφαιρα

    private List<HexTile>? _meltQueue;   // πολικός πάγος, χαμηλότερο υψόμετρο στο τέλος (pop από το τέλος)
    private List<HexTile>? _floodQueue;  // χαμηλά εδάφη για πλημμύρα από κομήτες
    private double _meltAccumulator;
    private double _floodAccumulator;
    private int _totalTiles;
    private int _waterTiles;

    public void Tick(World world)
    {
        var planet = world.Planet;

        bool shielded = false;
        foreach (var building in world.Colony.Buildings)
        {
            if (building.State != BuildingState.Operational) continue;
            if (building.Definition.ShieldsAtmosphere) shielded = true;

            if (building.Definition.PlanetEffects.Count == 0) continue;
            double eff = building.WorkerEfficiency();
            if (eff <= 0) continue;

            foreach (var (metric, delta) in building.Definition.PlanetEffects)
            {
                if (metric == PlanetMetric.Water)
                    _floodAccumulator += delta * eff;       // νερό κομητών → πλημμύρα χαμηλών εδαφών
                else
                    planet.Add(metric, delta * eff);
            }
        }

        // Χωρίς μαγνητόσφαιρα, ο ηλιακός άνεμος αφαιρεί ατμόσφαιρα.
        if (!shielded) planet.Add(PlanetMetric.Pressure, -PressureLeakPerTick);

        EnsureQueues(world);

        // Λιώσιμο πολικού πάγου ανάλογα με τη θερμοκρασία.
        if (planet.Temperature > MeltStartTemperature)
            _meltAccumulator += (planet.Temperature - MeltStartTemperature) * MeltTilesPerDegreePerTick;

        ConvertTiles(_meltQueue!, ref _meltAccumulator, world);
        ConvertTiles(_floodQueue!, ref _floodAccumulator, world);

        if (_totalTiles > 0)
            planet.SetWaterCoverage((double)_waterTiles / _totalTiles);
    }

    private void EnsureQueues(World world)
    {
        if (_meltQueue is not null) return;

        _totalTiles = world.Map.Count;
        _waterTiles = world.Map.Tiles.Count(t => t.Terrain == TerrainType.Water);

        // pop από το τέλος → χαμηλότερο υψόμετρο πρώτα (το νερό μαζεύεται στα χαμηλά)
        _meltQueue = world.Map.Tiles
            .Where(t => t.Terrain == TerrainType.PolarIce)
            .OrderByDescending(t => t.Elevation)
            .ToList();

        _floodQueue = world.Map.Tiles
            .Where(t => t.Terrain is TerrainType.Canyon or TerrainType.Lowland or TerrainType.Crater or TerrainType.Flatland)
            .OrderByDescending(t => t.Elevation)
            .ToList();
    }

    private void ConvertTiles(List<HexTile> queue, ref double accumulator, World world)
    {
        while (accumulator >= 1.0 && queue.Count > 0)
        {
            accumulator -= 1.0;
            var tile = queue[^1];
            queue.RemoveAt(queue.Count - 1);
            tile.Terrain = TerrainType.Water;
            _waterTiles++;
            world.BumpMapRevision();
        }
    }
}
