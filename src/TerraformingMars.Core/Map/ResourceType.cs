namespace TerraformingMars.Core.Map;

/// <summary>Φυσικός πόρος που μπορεί να βρίσκεται σε κοίτασμα ενός hex.</summary>
public enum ResourceType
{
    None,
    Ice,      // → νερό, οξυγόνο, υδρογόνο
    Iron,     // → δομικά υλικά, χάλυβας
    Silicon,  // → ηλεκτρονικά, ηλιακά panel
    Regolith  // → 3D printing υλικών κατασκευής
}
