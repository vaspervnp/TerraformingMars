using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Core.Colonists;

/// <summary>
/// Μέλος του πληρώματος. Stats (υγεία/ηθικό) θα επεκταθούν στη Φάση 6 (events/βλάβες).
/// Το <see cref="Assignment"/> ορίζεται μέσω <see cref="Simulation.Colony.Assign"/>.
/// </summary>
public sealed class Colonist
{
    public string Name { get; }
    public Specialty Specialty { get; }

    public double Health { get; internal set; } = 1.0;
    public double Morale { get; internal set; } = 1.0;

    public Building? Assignment { get; internal set; }

    public Colonist(string name, Specialty specialty)
    {
        Name = name;
        Specialty = specialty;
    }

    public override string ToString() => $"{Name} [{Specialty}]";
}
