using System.Linq;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Grid;

namespace TerraformingMars.Core.Buildings;

public enum BuildingState
{
    UnderConstruction,
    Operational,
    Disabled
}

/// <summary>
/// Instance κτιρίου στον χάρτη: θέση, κατάσταση κατασκευής/λειτουργίας, ανατεθειμένοι άποικοι.
/// </summary>
public sealed class Building
{
    public BuildingDefinition Definition { get; }
    public Hex Location { get; }
    public BuildingState State { get; internal set; }
    public int BuildProgress { get; internal set; }

    /// <summary>Υλικά που έχουν παραδοθεί μέχρι τώρα στο εργοτάξιο (από το συνολικό κόστος).</summary>
    public double MaterialsPaid { get; internal set; }

    /// <summary>True αν η κατασκευή έχει σταματήσει λόγω έλλειψης υλικών.</summary>
    public bool Stalled { get; internal set; }

    public List<Colonist> Workers { get; } = new();

    public Building(BuildingDefinition definition, Hex location, bool startOperational = false)
    {
        Definition = definition;
        Location = location;

        if (startOperational || !definition.Buildable)
        {
            State = BuildingState.Operational;
            BuildProgress = definition.BuildTimeTicks;
        }
        else
        {
            State = BuildingState.UnderConstruction;
            BuildProgress = 0;
        }
    }

    public double BuildFraction =>
        Definition.BuildTimeTicks <= 0 ? 1.0 : Math.Clamp((double)BuildProgress / Definition.BuildTimeTicks, 0, 1);

    /// <summary>
    /// Πολλαπλασιαστής απόδοσης [0..1.5]. Αυτόματα κτίρια (MaxWorkers=0) = 1.
    /// Στελεχωμένα: αναλογία στελέχωσης × (1 + 0.5 αν υπάρχει ο σωστός ειδικός).
    /// </summary>
    public double WorkerEfficiency()
    {
        if (State != BuildingState.Operational) return 0.0;
        if (Definition.MaxWorkers <= 0) return 1.0;
        if (Workers.Count == 0) return 0.0;

        double staffing = Math.Min(1.0, (double)Workers.Count / Definition.MaxWorkers);
        bool hasSpecialist = Definition.OptimalSpecialty != Specialty.None
                             && Workers.Any(w => w.Specialty == Definition.OptimalSpecialty);
        return staffing * (hasSpecialist ? 1.5 : 1.0);
    }

    public override string ToString() => $"{Definition.Name} @ {OffsetCoord.FromHex(Location)} ({State})";
}
