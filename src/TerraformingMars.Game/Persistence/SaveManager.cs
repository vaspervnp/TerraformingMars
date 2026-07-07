using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TerraformingMars.Game.Persistence;

/// <summary>
/// Διαχειρίζεται τον φάκελο <c>SavedGames</c>: πολλαπλά save, καθένα ως ζεύγος
/// <c>&lt;slug&gt;.json</c> (κατάσταση) + <c>&lt;slug&gt;.png</c> (screenshot της στιγμής αποθήκευσης).
/// </summary>
public static class SaveManager
{
    public static string Folder { get; } = Path.Combine(AppContext.BaseDirectory, "SavedGames");

    public static void EnsureFolder() => Directory.CreateDirectory(Folder);

    public static string JsonPath(string slug) => Path.Combine(Folder, slug + ".json");
    public static string PngPath(string slug) => Path.Combine(Folder, slug + ".png");

    /// <summary>Υπάρχει τουλάχιστον ένα save;</summary>
    public static bool HasAny() =>
        Directory.Exists(Folder) && Directory.EnumerateFiles(Folder, "*.json").Any();

    /// <summary>Τα slugs όλων των save (χωρίς επέκταση).</summary>
    public static IEnumerable<string> Slugs() =>
        Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*.json").Select(p => Path.GetFileNameWithoutExtension(p)!)
            : Enumerable.Empty<string>();

    public static void Delete(string slug)
    {
        try { File.Delete(JsonPath(slug)); } catch { /* ignore */ }
        try { File.Delete(PngPath(slug)); } catch { /* ignore */ }
    }

    /// <summary>Slug για χειροκίνητο save με σφραγίδα χρόνου (μοναδικό ανά δευτερόλεπτο).</summary>
    public static string ManualSlug(DateTime now) => "save_" + now.ToString("yyyyMMdd_HHmmss");

    /// <summary>Slug για αυτόματο save (κυκλικό 1..3).</summary>
    public static string AutoSlug(int slot) => "auto_" + slot;
}
