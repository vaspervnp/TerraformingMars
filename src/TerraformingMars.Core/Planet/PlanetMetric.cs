namespace TerraformingMars.Core.Planet;

/// <summary>Οι πλανητικές μετρικές που πρέπει να ανέβουν για terraforming.</summary>
public enum PlanetMetric
{
    Temperature,  // °C
    Pressure,     // kPa (ατμοσφαιρική)
    Oxygen,       // % της ατμόσφαιρας
    Water         // κάλυψη νερού (0..1)
}
