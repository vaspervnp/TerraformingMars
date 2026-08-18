using TerraformingMars.Core.Planet;
using Xunit;

namespace TerraformingMars.Core.Tests;

/// <summary>
/// Ένδειξη υπέρβασης στόχου (HUD): πόσο πάνω από τον στόχο βρίσκεται μια μετρική και πότε αυτό
/// γίνεται επικίνδυνο (runaway greenhouse). Ίδιο κατώφλι με το <see cref="Simulation.Phase2System"/>.
/// </summary>
public class OvershootTests
{
    private static PlanetState At(double temperature = PlanetState.StartTemperature,
                                  double pressure = PlanetState.StartPressure,
                                  double oxygen = PlanetState.StartOxygen,
                                  double water = PlanetState.StartWater)
    {
        var p = new PlanetState();
        p.Restore(temperature, pressure, oxygen, water, 0);
        return p;
    }

    [Fact]
    public void Below_Target_Reports_No_Overshoot()
    {
        var planet = At(temperature: -10, pressure: 5);

        Assert.Equal(OvershootLevel.None, planet.OvershootOf(PlanetMetric.Temperature));
        Assert.Equal(0, planet.Overshoot(PlanetMetric.Pressure));
    }

    [Fact]
    public void Exactly_On_Target_Is_Not_An_Overshoot()
    {
        var planet = At(temperature: PlanetState.TargetTemperature, pressure: PlanetState.TargetPressure);

        Assert.Equal(OvershootLevel.None, planet.OvershootOf(PlanetMetric.Temperature));
        Assert.Equal(OvershootLevel.None, planet.OvershootOf(PlanetMetric.Pressure));
    }

    [Fact]
    public void Above_Target_But_Under_The_Runaway_Threshold_Is_Only_Over()
    {
        var planet = At(temperature: PlanetState.TargetTemperature + 2);

        Assert.Equal(OvershootLevel.Over, planet.OvershootOf(PlanetMetric.Temperature));
        Assert.Equal(2, planet.Overshoot(PlanetMetric.Temperature), 3);
    }

    [Fact]
    public void Past_The_Runaway_Threshold_Is_Critical()
    {
        var planet = At(temperature: PlanetState.TargetTemperature + PlanetState.RunawayOvershoot + 1,
                        pressure: PlanetState.TargetPressure + PlanetState.RunawayOvershoot + 20);

        Assert.Equal(OvershootLevel.Critical, planet.OvershootOf(PlanetMetric.Temperature));
        Assert.Equal(OvershootLevel.Critical, planet.OvershootOf(PlanetMetric.Pressure));
    }

    [Fact]
    public void Oxygen_And_Water_Never_Go_Critical()
    {
        var planet = At(oxygen: PlanetState.TargetOxygen + 15, water: PlanetState.TargetWater + 0.5);

        Assert.Equal(OvershootLevel.Over, planet.OvershootOf(PlanetMetric.Oxygen));
        Assert.Equal(OvershootLevel.Over, planet.OvershootOf(PlanetMetric.Water));
        Assert.False(PlanetState.HasRunawayRisk(PlanetMetric.Oxygen));
        Assert.False(PlanetState.HasRunawayRisk(PlanetMetric.Water));
    }

    [Fact]
    public void Progress_Still_Caps_At_100_Percent_While_Overshoot_Keeps_Growing()
    {
        var planet = At(pressure: PlanetState.TargetPressure * 3);

        Assert.Equal(1.0, planet.Progress(PlanetMetric.Pressure), 3);          // η μπάρα δείχνει 100%
        Assert.Equal(PlanetState.TargetPressure * 2, planet.Overshoot(PlanetMetric.Pressure), 3); // η υπέρβαση όχι
    }

    [Fact]
    public void Targets_Are_Exposed_Per_Metric()
    {
        Assert.Equal(PlanetState.TargetTemperature, PlanetState.TargetOf(PlanetMetric.Temperature));
        Assert.Equal(PlanetState.TargetPressure, PlanetState.TargetOf(PlanetMetric.Pressure));
        Assert.Equal(PlanetState.TargetOxygen, PlanetState.TargetOf(PlanetMetric.Oxygen));
        Assert.Equal(PlanetState.TargetWater, PlanetState.TargetOf(PlanetMetric.Water));
    }
}
