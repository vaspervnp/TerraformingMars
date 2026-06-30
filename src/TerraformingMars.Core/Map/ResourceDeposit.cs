namespace TerraformingMars.Core.Map;

/// <summary>
/// Κοίτασμα πόρου σε ένα hex. Άκαμπτο struct.
/// <see cref="Hidden"/> = δεν φαίνεται μέχρι να το εντοπίσει Γεωλόγος / survey.
/// </summary>
public readonly struct ResourceDeposit
{
    public ResourceType Type { get; }
    public int Amount { get; }
    public bool Hidden { get; }

    public ResourceDeposit(ResourceType type, int amount, bool hidden = false)
    {
        Type = type;
        Amount = amount;
        Hidden = hidden;
    }

    public static readonly ResourceDeposit Empty = new(ResourceType.None, 0);

    public bool IsEmpty => Type == ResourceType.None || Amount <= 0;
}
