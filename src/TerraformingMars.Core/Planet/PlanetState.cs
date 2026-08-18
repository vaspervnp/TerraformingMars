namespace TerraformingMars.Core.Planet;

/// <summary>
/// Παγκόσμια κατάσταση του πλανήτη. Αρχικές τιμές ≈ σημερινός Άρης· στόχος να φτάσουν
/// τα κατώφλια κατοικησιμότητας. Το <see cref="OverallProgress"/> = συνολικό terraforming.
/// </summary>
public sealed class PlanetState
{
    // Αρχικές τιμές (Άρης)
    public const double StartTemperature = -60.0;
    public const double StartPressure = 0.6;
    public const double StartOxygen = 0.1;
    public const double StartWater = 0.0;

    // Στόχοι κατοικησιμότητας
    public const double TargetTemperature = 0.0;
    public const double TargetPressure = 10.0;
    public const double TargetOxygen = 15.0;
    public const double TargetWater = 0.30;

    public double Temperature { get; private set; } = StartTemperature;
    public double Pressure { get; private set; } = StartPressure;
    public double Oxygen { get; private set; } = StartOxygen;
    public double WaterCoverage { get; private set; } = StartWater;

    /// <summary>Κάλυψη βλάστησης (0..1) — δείκτης οικοσυστήματος (Φάση 7), εκτός των μετρικών νίκης.</summary>
    public double Biomass { get; private set; }
    public void SetBiomass(double fraction) => Biomass = Math.Clamp(fraction, 0, 1);

    /// <summary>Επαναφορά όλων των τιμών (για load παιχνιδιού).</summary>
    public void Restore(double temperature, double pressure, double oxygen, double water, double biomass)
    {
        Temperature = temperature;
        Pressure = pressure;
        Oxygen = oxygen;
        WaterCoverage = water;
        Biomass = biomass;
    }

    public double Get(PlanetMetric metric) => metric switch
    {
        PlanetMetric.Temperature => Temperature,
        PlanetMetric.Pressure => Pressure,
        PlanetMetric.Oxygen => Oxygen,
        PlanetMetric.Water => WaterCoverage,
        _ => 0
    };

    public void Add(PlanetMetric metric, double delta)
    {
        switch (metric)
        {
            case PlanetMetric.Temperature: Temperature = Math.Clamp(Temperature + delta, -120, 60); break;
            case PlanetMetric.Pressure: Pressure = Math.Max(0, Pressure + delta); break;
            case PlanetMetric.Oxygen: Oxygen = Math.Clamp(Oxygen + delta, 0, 100); break;
            case PlanetMetric.Water: WaterCoverage = Math.Clamp(WaterCoverage + delta, 0, 1); break;
        }
    }

    /// <summary>Τίθεται από το <see cref="Simulation.PlanetSystem"/> με βάση τα tiles νερού του χάρτη.</summary>
    public void SetWaterCoverage(double fraction) => WaterCoverage = Math.Clamp(fraction, 0, 1);

    /// <summary>Πρόοδος μιας μετρικής προς τον στόχο της (0..1).</summary>
    public double Progress(PlanetMetric metric)
    {
        (double value, double start, double target) = metric switch
        {
            PlanetMetric.Temperature => (Temperature, StartTemperature, TargetTemperature),
            PlanetMetric.Pressure => (Pressure, StartPressure, TargetPressure),
            PlanetMetric.Oxygen => (Oxygen, StartOxygen, TargetOxygen),
            PlanetMetric.Water => (WaterCoverage, StartWater, TargetWater),
            _ => (0, 0, 1)
        };
        if (target <= start) return 1;
        return Math.Clamp((value - start) / (target - start), 0, 1);
    }

    /// <summary>Μέσος όρος προόδου των 4 μετρικών (0..1).</summary>
    public double OverallProgress =>
        (Progress(PlanetMetric.Temperature) + Progress(PlanetMetric.Pressure) +
         Progress(PlanetMetric.Oxygen) + Progress(PlanetMetric.Water)) / 4.0;

    public bool IsTerraformed =>
        Progress(PlanetMetric.Temperature) >= 1 && Progress(PlanetMetric.Pressure) >= 1 &&
        Progress(PlanetMetric.Oxygen) >= 1 && Progress(PlanetMetric.Water) >= 1;

    // ---------------------------------------------------------------- υπέρβαση στόχου

    /// <summary>Πόσο πάνω από τον στόχο ξεκινά το runaway greenhouse (°C / kPa) — βλ. Phase2System.</summary>
    public const double RunawayOvershoot = 4.0;

    /// <summary>Ο στόχος κατοικησιμότητας μιας μετρικής (στις μονάδες της μετρικής).</summary>
    public static double TargetOf(PlanetMetric metric) => metric switch
    {
        PlanetMetric.Temperature => TargetTemperature,
        PlanetMetric.Pressure => TargetPressure,
        PlanetMetric.Oxygen => TargetOxygen,
        PlanetMetric.Water => TargetWater,
        _ => 0
    };

    /// <summary>Μετρικές που, αν ξεφύγουν προς τα πάνω, πυροδοτούν runaway greenhouse (Φάση 2).</summary>
    public static bool HasRunawayRisk(PlanetMetric metric) =>
        metric is PlanetMetric.Temperature or PlanetMetric.Pressure;

    /// <summary>Πόσο πάνω από τον στόχο βρίσκεται η μετρική (0 αν είναι ακόμη κάτω).</summary>
    public double Overshoot(PlanetMetric metric) => Math.Max(0, Get(metric) - TargetOf(metric));

    /// <summary>
    /// Πόσο «πάνω από το ιδανικό» είναι μια μετρική τώρα — για ένδειξη στο HUD ώστε ο παίκτης να
    /// βλέπει την υπέρβαση σε πραγματικό χρόνο, όχι μόνο όταν σκάσει το runaway στη Φάση 2.
    /// </summary>
    public OvershootLevel OvershootOf(PlanetMetric metric)
    {
        double over = Overshoot(metric);
        if (over <= 0) return OvershootLevel.None;
        return HasRunawayRisk(metric) && over > RunawayOvershoot ? OvershootLevel.Critical : OvershootLevel.Over;
    }
}
