using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TerraformingMars.Game.Persistence;

/// <summary>
/// Διαχειρίζεται τον φάκελο <c>SavedGames</c>: πολλαπλά save, καθένα ως ζεύγος
/// <c>&lt;slug&gt;.json</c> (κατάσταση) + <c>&lt;slug&gt;.png</c> (screenshot της στιγμής αποθήκευσης).
/// <para>
/// Τα save ζουν στον λογαριασμό του χρήστη (<c>%APPDATA%\Mars Terraforming\SavedGames</c>, σε
/// Linux/macOS το αντίστοιχο), όχι δίπλα στο εκτελέσιμο: έτσι επιβιώνουν από
/// rebuild/επανεγκατάσταση και δουλεύουν και όταν ο φάκελος του παιχνιδιού είναι read-only.
/// </para>
/// </summary>
public static class SaveManager
{
    private const string GameFolderName = "Mars Terraforming";      // Windows / macOS
    private const string GameFolderNameXdg = "MarsTerraforming";    // Linux (χωρίς κενά, XDG στιλ)

    // Παλιά ονόματα (πριν τη μετονομασία του παιχνιδιού) — μόνο για μεταφορά των save.
    private const string OldGameFolderName = "Terraforming Mars";
    private const string OldGameFolderNameXdg = "TerraformingMars";

    /// <summary>Ο φάκελος του παιχνιδιού μέσα στον λογαριασμό του χρήστη.</summary>
    public static string GameFolder { get; } = AccountFolder(GameFolderName, GameFolderNameXdg);

    public static string Folder { get; } = Path.Combine(GameFolder, "SavedGames");

    /// <summary>
    /// Θέσεις προηγούμενων εκδόσεων: δίπλα στο εκτελέσιμο, και ο φάκελος με το παλιό όνομα του
    /// παιχνιδιού. Χρησιμοποιούνται μόνο από το <see cref="MigrateLegacySaves"/>.
    /// </summary>
    public static IReadOnlyList<string> LegacyFolders { get; } = new[]
    {
        Path.Combine(AppContext.BaseDirectory, "SavedGames"),
        Path.Combine(AccountFolder(OldGameFolderName, OldGameFolderNameXdg), "SavedGames"),
    };

    /// <summary>
    /// Πού «ανήκουν» τα δεδομένα του παιχνιδιού σε κάθε πλατφόρμα:
    /// <list type="bullet">
    /// <item>Windows: <c>%APPDATA%\Mars Terraforming</c></item>
    /// <item>Linux: <c>$XDG_DATA_HOME</c> ή <c>~/.local/share/MarsTerraforming</c> (save = data, όχι config)</item>
    /// <item>macOS: <c>~/Library/Application Support/Mars Terraforming</c></item>
    /// </list>
    /// Αν για κάποιον λόγο δεν βρεθεί home directory, πέφτουμε πίσω δίπλα στο εκτελέσιμο.
    /// </summary>
    private static string AccountFolder(string name, string xdgName)
    {
        if (OperatingSystem.IsWindows())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);

        if (OperatingSystem.IsMacOS())
            return Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), name);

        // Linux & λοιπά Unix: XDG_DATA_HOME (το .NET το επιστρέφει ως LocalApplicationData), αλλιώς ~/.local/share.
        string xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME") ?? "";
        if (xdg.Length == 0) xdg = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (xdg.Length == 0)
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (home.Length > 0) xdg = Path.Combine(home, ".local", "share");
        }
        return Combine(xdg, xdgName);

        static string Combine(string root, string folder) => string.IsNullOrEmpty(root)
            ? Path.Combine(AppContext.BaseDirectory, folder)   // έσχατη λύση: δίπλα στο παιχνίδι
            : Path.Combine(root, folder);
    }

    public static void EnsureFolder() => Directory.CreateDirectory(Folder);

    /// <summary>
    /// Μεταφέρει (στο ξεκίνημα) τα save που βρίσκονται ακόμη σε θέση προηγούμενης έκδοσης — δίπλα
    /// στο εκτελέσιμο ή στον φάκελο με το παλιό όνομα του παιχνιδιού — μέσα στον σημερινό φάκελο.
    /// Ένα save που υπάρχει ήδη στη νέα θέση δεν πατιέται· το παλιό αρχείο μένει εκεί που είναι.
    /// Επιστρέφει πόσα μεταφέρθηκαν συνολικά.
    /// </summary>
    public static int MigrateLegacySaves() => LegacyFolders.Sum(MigrateFrom);

    private static int MigrateFrom(string legacyFolder)
    {
        if (string.Equals(Path.GetFullPath(legacyFolder), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            return 0;
        if (!Directory.Exists(legacyFolder)) return 0;

        int moved = 0;
        try
        {
            foreach (string oldJson in Directory.EnumerateFiles(legacyFolder, "*.json"))
            {
                string slug = Path.GetFileNameWithoutExtension(oldJson)!;
                if (File.Exists(JsonPath(slug))) continue;       // η νέα θέση έχει τον λόγο

                EnsureFolder();
                string oldPng = Path.Combine(legacyFolder, slug + ".png");
                File.Move(oldJson, JsonPath(slug));
                if (File.Exists(oldPng)) File.Move(oldPng, PngPath(slug), overwrite: true);
                moved++;
            }

            // Αν άδειασε τελείως, μάζεψε και τον παλιό φάκελο (και τον γονέα του, αν έμεινε άδειος).
            if (Directory.Exists(legacyFolder) && !Directory.EnumerateFileSystemEntries(legacyFolder).Any())
            {
                Directory.Delete(legacyFolder);
                string? parent = Path.GetDirectoryName(legacyFolder);
                if (parent is not null && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any()
                    && !string.Equals(Path.GetFullPath(parent), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(parent);
            }
        }
        catch (IOException) { /* ό,τι πρόλαβε να μεταφερθεί μένει· το παιχνίδι συνεχίζει */ }
        catch (UnauthorizedAccessException) { }
        return moved;
    }

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
