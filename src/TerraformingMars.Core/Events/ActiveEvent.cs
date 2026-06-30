namespace TerraformingMars.Core.Events;

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
