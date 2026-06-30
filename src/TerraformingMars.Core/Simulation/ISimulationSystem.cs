namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Ένα κομμάτι λογικής που τρέχει μία φορά ανά tick. Τα systems εκτελούνται με τη σειρά
/// που έχουν προστεθεί στον <see cref="World"/>. Νέος μηχανισμός = νέο system.
/// </summary>
public interface ISimulationSystem
{
    void Tick(World world);
}
