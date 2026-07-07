namespace TerraformingMars.Core.Events;

/// <summary>
/// Ειδοποίηση προς το UI ότι ένα γεγονός μόλις ξεκίνησε: ο τύπος του και η εκτιμώμενη
/// διάρκειά του σε ticks (0 = μη χρονισμένο, π.χ. μόνιμο εφέ). Είναι εφήμερη — το UI την
/// «καταναλώνει» για να δείξει popup και δεν αποθηκεύεται στο save.
/// </summary>
public readonly record struct EventStart(EventType Type, int DurationTicks);

/// <summary>Ένα ενεργό χρονισμένο γεγονός (π.χ. αμμοθύελλα/έκλαμψη) με χρόνο που απομένει.</summary>
public sealed class ActiveEvent
{
    public EventType Type { get; }
    public int TicksRemaining { get; set; }

    public ActiveEvent(EventType type, int ticksRemaining)
    {
        Type = type;
        TicksRemaining = ticksRemaining;
    }
}
