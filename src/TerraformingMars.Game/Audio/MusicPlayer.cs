using System;
using System.IO;
using Microsoft.Xna.Framework.Media;

namespace TerraformingMars.Game.Audio;

/// <summary>
/// Αναπαραγωγή μουσικής υποβάθρου (loop) μέσω του cross-platform <see cref="MediaPlayer"/> του
/// MonoGame. Παίζει <b>OGG Vorbis</b> (αποκωδικοποιείται με NVorbis, μέσω OpenAL) ώστε να δουλεύει
/// σε Linux/mac/Windows — σε αντίθεση με το NAudio που ήθελε Windows Media Foundation για MP3.
/// Όλα σε try/catch ώστε να υποβαθμίζεται σιωπηλά αν δεν υπάρχει audio device / codec.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private Song? _song;
    private string? _currentPath;
    private float _volume = 0.6f;
    private bool _muted;

    /// <summary>Πλήρης διαδρομή του τρέχοντος κομματιού (ή null αν δεν παίζει τίποτα).</summary>
    public string? CurrentPath => _currentPath;

    /// <summary>Ένταση 0..1 (πριν το mute).</summary>
    public float Volume
    {
        get => _volume;
        set { _volume = Math.Clamp(value, 0f, 1f); ApplyVolume(); }
    }

    public bool Muted
    {
        get => _muted;
        set { _muted = value; ApplyVolume(); }
    }

    /// <summary>Ξεκινά (σε loop) το αρχείο <paramref name="path"/>. Σταματά ό,τι έπαιζε πριν.</summary>
    public void Play(string? path)
    {
        Stop();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            _song = Song.FromUri(Path.GetFileNameWithoutExtension(path), new Uri(path, UriKind.Absolute));
            MediaPlayer.IsRepeating = true;
            ApplyVolume();
            MediaPlayer.Play(_song);
            _currentPath = path;
        }
        catch
        {
            Stop(); // λείπει codec/device → σιωπηλά χωρίς μουσική
        }
    }

    public void Stop()
    {
        try { MediaPlayer.Stop(); } catch { /* ignore */ }
        _song?.Dispose();
        _song = null;
        _currentPath = null;
    }

    private void ApplyVolume()
    {
        try { MediaPlayer.Volume = _muted ? 0f : _volume; } catch { /* ignore */ }
    }

    public void Dispose() => Stop();
}
