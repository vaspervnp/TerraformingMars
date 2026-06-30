namespace TerraformingMars.Core.Simulation;

/// <summary>Ταχύτητα προσομοίωσης. <see cref="Paused"/> = ο χρόνος δεν προχωρά.</summary>
public enum GameSpeed
{
    Paused,
    Normal,
    Fast,
    Ultra
}

public static class GameSpeedExtensions
{
    public static double Multiplier(this GameSpeed speed) => speed switch
    {
        GameSpeed.Paused => 0.0,
        GameSpeed.Normal => 1.0,
        GameSpeed.Fast   => 2.0,
        GameSpeed.Ultra  => 4.0,
        _ => 1.0
    };
}
