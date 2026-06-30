namespace TerraformingMars.Core.Buildings;

/// <summary>Αποτέλεσμα προσπάθειας τοποθέτησης κτιρίου: επιτυχία+instance ή αποτυχία+λόγος.</summary>
public readonly struct PlacementResult
{
    public bool Success { get; }
    public string? Error { get; }
    public Building? Building { get; }

    private PlacementResult(bool success, string? error, Building? building)
    {
        Success = success;
        Error = error;
        Building = building;
    }

    public static PlacementResult Ok(Building building) => new(true, null, building);
    public static PlacementResult Fail(string error) => new(false, error, null);
}
