namespace TerraformingMars.Core.Events;

/// <summary>
/// Προφίλ χορηγού = επίπεδο δυσκολίας. Κλιμακώνει αρχικούς πόρους, συχνότητα/διάρκεια
/// γεγονότων και χρόνο επισκευής. Data-driven (από <c>Data/sponsors.json</c>).
/// </summary>
public sealed class SponsorProfile
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";

    public double StartingResourceMultiplier { get; init; } = 1.0;

    /// <summary>Βασική χωρητικότητα στέγασης (όριο αρχικού πληθυσμού) — κλιμακώνεται με τη δυσκολία.</summary>
    public int BaseHousing { get; init; } = 12;

    public double EventChancePerTick { get; init; } = 0.0008;
    public double DustStormSolarFactor { get; init; } = 0.2;

    public int DustStormMinTicks { get; init; } = 300;
    public int DustStormMaxTicks { get; init; } = 600;
    public int SolarFlareMinTicks { get; init; } = 150;
    public int SolarFlareMaxTicks { get; init; } = 300;
    public int RepairTicks { get; init; } = 300;
}
