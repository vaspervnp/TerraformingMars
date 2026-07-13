using TerraformingMars.Core.Map;
using TerraformingMars.Core.Planet;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Ο βρόχος της Φάσης 2 («The Living Planet»): αφηρημένη μετανάστευση πληθυσμού και το
/// <b>runaway greenhouse</b> — όταν Temperature/Pressure ξεπερνούν αισθητά τον στόχο (επειδή τα
/// macro-engineering κτίρια μένουν αναμμένα), προκαλείται κλιμακούμενη ζημιά: φθορά υγείας
/// αποίκων, μαράζωμα βλάστησης και εξάτμιση νερού. Ο παίκτης το αντιμετωπίζει με Cryo-Carbon
/// Capturers. Τρέχει ΜΕΤΑ τα PlanetSystem/BiosphereSystem ώστε να διαβάζει τις τελικές τιμές του tick.
/// Ντετερμινιστικό (καμία τυχαιότητα): οι επιλογές tiles γίνονται με ταξινόμηση υψομέτρου.
/// </summary>
public sealed class Phase2System : ISimulationSystem
{
    private const double MigrationPerTick = 2.0;          // ~288 άτομα/Sol (144 ticks/Sol)

    private const double RunawayTempThreshold = 4.0;      // °C πάνω από τον στόχο για έναρξη runaway
    private const double RunawayPressureThreshold = 4.0;  // kPa πάνω από τον στόχο για έναρξη runaway
    private const double SeverityScale = 15.0;            // overshoot που δίνει severity 1.0
    private const double MaxSeverity = 3.0;

    // Πρέπει να υπερισχύει της ambient αναγέννησης υγείας του EventSystem (0.0015/tick), αλλιώς
    // ένα ήπιο runaway δεν «αρρωσταίνει» ποτέ τους αποίκους. Στο κατώφλι (severity ~0.27) το
    // 0.008*0.27 ≈ 0.0022 > 0.0015 ⇒ καθαρή πτώση· σε ισχυρό runaway (severity 3) ~0.024/tick.
    private const double HealthDecayPerTick = 0.008;
    private const double HealthFloor = 0.25;              // το runaway ΔΕΝ σκοτώνει — μόνο εξασθενεί
    private const double HealthRecoveryPerTick = 0.0004;

    private const double WitherTilesPerTick = 1.5;
    private const double VaporizeTilesPerTick = 1.0;

    public void Tick(World world)
    {
        if (!world.Phase2Active) return;

        var colony = world.Colony;
        var planet = world.Planet;

        // Μετανάστευση: ο αφηρημένος πληθυσμός μεγαλώνει (κλασματικά, σώζεται ως double στο Colony).
        colony.Population += MigrationPerTick;

        double tempOver = Math.Max(0, planet.Temperature - PlanetState.TargetTemperature);
        double pressOver = Math.Max(0, planet.Pressure - PlanetState.TargetPressure);
        bool runaway = tempOver > RunawayTempThreshold || pressOver > RunawayPressureThreshold;
        world.RunawayActive = runaway;

        if (runaway)
        {
            double severity = Math.Min(MaxSeverity, Math.Max(tempOver, pressOver) / SeverityScale);

            foreach (var c in colony.Colonists)
                c.Health = Math.Max(HealthFloor, c.Health - HealthDecayPerTick * severity);

            FlipHighest(world, TerrainType.Vegetation, (int)Math.Ceiling(severity * WitherTilesPerTick));
            FlipHighest(world, TerrainType.Water, (int)Math.Ceiling(severity * VaporizeTilesPerTick));
        }
        else
        {
            // Ήπια ανάκαμψη μόλις το κλίμα επιστρέψει στο sweet spot (το runaway είναι αναστρέψιμο).
            foreach (var c in colony.Colonists)
                c.Health = Math.Min(1.0, c.Health + HealthRecoveryPerTick);
        }
    }

    /// <summary>Μετατρέπει τα <paramref name="count"/> tiles υψηλότερου υψομέτρου του τύπου <paramref name="from"/>
    /// σε Flatland (μαράζωμα/εξάτμιση). Ο πάγος δεν ξαναλιώνει άμεσα (η ουρά melt είναι one-shot)· η
    /// βλάστηση μπορεί να αναγεννηθεί όταν το κλίμα επανέλθει στο habitable band (η ουρά growth ξαναχτίζεται).</summary>
    private static void FlipHighest(World world, TerrainType from, int count)
    {
        if (count <= 0) return;
        var targets = world.Map.Tiles
            .Where(t => t.Terrain == from)
            .OrderByDescending(t => t.Elevation)
            .Take(count)
            .ToList();
        foreach (var t in targets) t.Terrain = TerrainType.Flatland;
        if (targets.Count > 0) world.BumpMapRevision();
    }
}
