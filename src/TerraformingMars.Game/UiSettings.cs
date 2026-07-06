using System;
using System.IO;
using System.Text.Json;

namespace TerraformingMars.Game;

/// <summary>
/// Ρυθμίσεις διεπαφής που διατηρούνται ανάμεσα σε εκτελέσεις (JSON δίπλα στο εκτελέσιμο).
/// Προς το παρόν κρατά τη θέση (πάνω-αριστερή γωνία) του panel πληροφοριών κτηρίου, ώστε
/// να θυμάται πού το άφησε ο παίκτης. <c>null</c> σημαίνει «καμία αποθηκευμένη θέση» → προεπιλογή.
/// </summary>
public sealed class UiSettings
{
    public int? BuildingPanelX { get; set; }
    public int? BuildingPanelY { get; set; }

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "ui_settings.json");

    public static UiSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(FilePath)) ?? new UiSettings();
        }
        catch { /* κατεστραμμένο αρχείο → defaults */ }
        return new UiSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* read-only path → αγνόησε */ }
    }
}
