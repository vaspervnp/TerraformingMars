namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Λογιστικό αποθεμάτων: ποσότητες ανά πόρο, χωρητικότητες αποθήκευσης
/// (μπαταρίες/δεξαμενές — default «άπειρο» αν δεν οριστεί), και net rate/tick για το UI.
/// </summary>
public sealed class ResourceLedger
{
    private readonly Dictionary<ResourceKind, double> _amount = new();
    private readonly Dictionary<ResourceKind, double> _capacity = new();
    private readonly Dictionary<ResourceKind, double> _rate = new();

    public double Get(ResourceKind k) => _amount.GetValueOrDefault(k);
    public double RatePerTick(ResourceKind k) => _rate.GetValueOrDefault(k);

    public bool HasCapacityLimit(ResourceKind k) => _capacity.ContainsKey(k);
    public double Capacity(ResourceKind k) => _capacity.TryGetValue(k, out var c) ? c : double.PositiveInfinity;

    /// <summary>Προσθέτει χωρητικότητα αποθήκευσης (π.χ. νέα μπαταρία/δεξαμενή).</summary>
    public void AddCapacity(ResourceKind k, double delta) =>
        _capacity[k] = _capacity.GetValueOrDefault(k) + delta;

    public void Set(ResourceKind k, double value) => _amount[k] = Clamp(k, value);

    /// <summary>Προσθέτει (ή αφαιρεί) ποσότητα με clamp σε [0, capacity]. Επιστρέφει την πραγματική μεταβολή.</summary>
    public double Add(ResourceKind k, double delta)
    {
        double before = Get(k);
        double after = Clamp(k, before + delta);
        _amount[k] = after;
        return after - before;
    }

    /// <summary>Καταναλώνει μόνο αν υπάρχει αρκετό απόθεμα. Αλλιώς δεν αλλάζει τίποτα.</summary>
    public bool TryConsume(ResourceKind k, double amount)
    {
        if (amount <= 0) return true;
        if (Get(k) + 1e-9 < amount) return false;
        _amount[k] = Get(k) - amount;
        return true;
    }

    internal void RecordRate(ResourceKind k, double netPerTick) => _rate[k] = netPerTick;

    private double Clamp(ResourceKind k, double value)
    {
        if (value < 0) value = 0;
        double cap = Capacity(k);
        return value > cap ? cap : value;
    }
}
