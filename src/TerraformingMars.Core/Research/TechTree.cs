namespace TerraformingMars.Core.Research;

/// <summary>
/// Κατάσταση έρευνας μιας αποικίας: τι έχει ερευνηθεί, ποιο είναι το τρέχον target,
/// και η πρόοδος προς αυτό. Τα prerequisites επιβάλλονται εδώ.
/// </summary>
public sealed class TechTree
{
    private readonly TechCatalog _catalog;

    public HashSet<string> Researched { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? CurrentTarget { get; private set; }
    public double CurrentProgress { get; private set; }

    public TechTree(TechCatalog? catalog = null) => _catalog = catalog ?? TechCatalog.LoadDefault();

    public TechCatalog Catalog => _catalog;

    public bool IsResearched(string techId) =>
        string.IsNullOrEmpty(techId) || Researched.Contains(techId);

    public bool ArePrerequisitesMet(TechDefinition tech) => tech.Prerequisites.All(IsResearched);

    public bool CanResearch(TechDefinition tech) =>
        !Researched.Contains(tech.Id) && ArePrerequisitesMet(tech);

    /// <summary>Τεχνολογίες που μπορούν να ερευνηθούν τώρα (prereqs met, όχι ήδη researched).</summary>
    public IEnumerable<TechDefinition> Available =>
        _catalog.All.Where(CanResearch).OrderBy(t => t.Phase).ThenBy(t => t.Cost);

    public TechDefinition? CurrentTech => CurrentTarget is null ? null : _catalog.Get(CurrentTarget);

    /// <summary>Θέτει το target έρευνας (αν επιτρέπεται). Μηδενίζει την πρόοδο.</summary>
    public bool StartResearch(string techId)
    {
        if (!_catalog.TryGet(techId, out var tech) || tech is null) return false;
        if (!CanResearch(tech)) return false;

        CurrentTarget = tech.Id;
        CurrentProgress = 0;
        return true;
    }

    /// <summary>Προσθέτει research points στο τρέχον target. Όταν φτάσει το cost → researched.</summary>
    public void AddProgress(double points)
    {
        if (CurrentTarget is null || points <= 0) return;

        CurrentProgress += points;
        var tech = _catalog.Get(CurrentTarget);
        if (CurrentProgress >= tech.Cost)
        {
            Researched.Add(tech.Id);
            CurrentTarget = null;
            CurrentProgress = 0;
        }
    }

    /// <summary>Building ids που έχουν ξεκλειδωθεί από τις researched τεχνολογίες.</summary>
    public IEnumerable<string> UnlockedBuildingIds =>
        _catalog.All.Where(t => Researched.Contains(t.Id)).SelectMany(t => t.Unlocks);
}
