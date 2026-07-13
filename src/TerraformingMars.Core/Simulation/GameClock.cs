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

    /// <summary>
    /// Μήκος ενός Sol σε ticks — για μηχανισμούς της σιμουλασιόν (π.χ. φθορά reclaim ανά Sol).
    /// Αμετάβλητο: ο ρυθμός της σιμουλασιόν μένει ίδιος· αλλάζει μόνο η ένδειξη του ρολογιού.
    /// </summary>
    public const double TicksPerSol = 144.0;

    /// <summary>Ώρες που δείχνει το ρολόι ανά tick — η ένδειξη προχωρά σε βήματα 1 ώρας.</summary>
    public const int DisplayHoursPerTick = 1;

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

    // --- Ένδειξη ρολογιού: κάθε tick προχωρά τον χρόνο κατά 1 ώρα (24 ώρες = 1 Sol) ---
    public long TotalDisplayHours => TotalTicks * DisplayHoursPerTick;
    public int Sol => (int)(TotalDisplayHours / 24) + 1;                      // 1-based
    public int HourOfSol => (int)(TotalDisplayHours % 24);
    public int MinuteOfHour => 0;                                            // ακέραιες ώρες
}
