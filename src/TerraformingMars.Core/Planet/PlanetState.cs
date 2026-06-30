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
}
