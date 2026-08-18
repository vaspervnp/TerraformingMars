namespace TerraformingMars.Core.Planet;

/// <summary>
/// Πόσο έχει ξεπεράσει μια μετρική τον στόχο κατοικησιμότητας.
/// <see cref="Over"/> = πάνω από τον στόχο (καμία ζημιά ακόμη, αλλά χάνεται απόδοση λόγω κορεσμού),
/// <see cref="Critical"/> = πάνω από το κατώφλι του runaway greenhouse (θερμοκρασία/πίεση).
/// </summary>
public enum OvershootLevel
{
    None,
    Over,
    Critical
}
