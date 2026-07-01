using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TerraformingMars.Game.Audio;

/// <summary>
/// Ρυθμίσεις ήχου που επιλέγονται στο μενού και διατηρούνται ανάμεσα σε εκτελέσεις
/// (JSON δίπλα στο εκτελέσιμο). Το <see cref="MusicTrack"/> είναι το όνομα αρχείου στο Assets
/// (ή <see cref="NoTrack"/> για καθόλου μουσική).
/// </summary>
public sealed class AudioSettings
{
    public const string NoTrack = "None";

    public string MusicTrack { get; set; } = "";
    public float MusicVolume { get; set; } = 0.6f;
    public bool MusicMuted { get; set; }
    public bool SfxEnabled { get; set; } = true;
    public float SfxVolume { get; set; } = 0.8f;

    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".ogg", ".aiff", ".wma", ".m4a", ".aac", ".flac" };

    private static string FilePath => Path.Combine(AppContext.BaseDirectory, "audio_settings.json");

    /// <summary>Φάκελος με τα κομμάτια μουσικής (Assets δίπλα στο εκτελέσιμο).</summary>
    public static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "Assets");

    /// <summary>Ονόματα αρχείων ήχου στο Assets (με "None" πρώτο), για επιλογή στο μενού.</summary>
    public static List<string> AvailableTracks()
    {
        var tracks = new List<string> { NoTrack };
        try
        {
            if (Directory.Exists(AssetsDir))
                tracks.AddRange(Directory.EnumerateFiles(AssetsDir)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Select(f => Path.GetFileName(f)!)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        }
        catch { /* χωρίς Assets → μόνο "None" */ }
        return tracks;
    }

    /// <summary>Πλήρης διαδρομή για ένα όνομα κομματιού (null για "None"/άγνωστο).</summary>
    public static string? PathFor(string track) =>
        string.IsNullOrEmpty(track) || track == NoTrack ? null : Path.Combine(AssetsDir, track);

    public static AudioSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AudioSettings>(File.ReadAllText(FilePath)) ?? new AudioSettings();
        }
        catch { /* κατεστραμμένο αρχείο → defaults */ }
        return new AudioSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* read-only path → αγνόησε */ }
    }
}
