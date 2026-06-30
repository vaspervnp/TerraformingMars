namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Fixed-timestep ρολόι. Μετατρέπει πραγματικό χρόνο × ταχύτητα σε διακριτά ticks
/// με accumulator (ντετερμινιστικό, ανεξάρτητο από framerate). 1 tick = σταθερό
/// κομμάτι in-game χρόνου ⇒ η σιμουλασιόν αναπαράγεται ίδια για ίδιες εισόδους.
/// </summary>
public sealed class GameClock
{
    /// <summary>Ticks ανά πραγματικό δευτερόλεπτο στο ×1.</summary>
    public const double TicksPerSecondAtNormal = 4.0;

    /// <summary>In-game λεπτά ανά tick (144 ticks ≈ 1 Sol 24h).</summary>
    public const double InGameMinutesPerTick = 10.0;

    private const double MinutesPerSol = 24 * 60;

    public GameSpeed Speed { get; set; } = GameSpeed.Normal;
    public long TotalTicks { get; private set; }

    private double _accumulator;

    /// <summary>
    /// Προωθεί το ρολόι κατά <paramref name="realDeltaSeconds"/> και επιστρέφει πόσα
    /// ολόκληρα ticks πρέπει να τρέξουν. Με όριο για αποφυγή "spiral of death" σε lag spikes.
    /// </summary>
    public int Advance(double realDeltaSeconds, int maxTicksPerFrame = 32)
    {
        double mult = Speed.Multiplier();
        if (mult <= 0.0)
        {
            _accumulator = 0.0;
            return 0;
        }

        _accumulator += realDeltaSeconds * mult * TicksPerSecondAtNormal;

        int ticks = (int)_accumulator;
        if (ticks > maxTicksPerFrame) ticks = maxTicksPerFrame;
        _accumulator -= ticks;

        TotalTicks += ticks;
        return ticks;
    }

    /// <summary>Επαναφορά μετρητή ticks (για load παιχνιδιού).</summary>
    public void RestoreTicks(long ticks) => TotalTicks = ticks;

    public double TotalInGameMinutes => TotalTicks * InGameMinutesPerTick;
    public int Sol => (int)(TotalInGameMinutes / MinutesPerSol) + 1;          // 1-based
    public int HourOfSol => (int)(TotalInGameMinutes / 60 % 24);
    public int MinuteOfHour => (int)(TotalInGameMinutes % 60);
}
