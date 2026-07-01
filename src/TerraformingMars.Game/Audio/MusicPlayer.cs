using System;
using System.IO;
using NAudio.Wave;

namespace TerraformingMars.Game.Audio;

/// <summary>
/// Αναπαραγωγή μουσικής υποβάθρου (loop) μέσω NAudio. Διαβάζει οποιοδήποτε αρχείο υποστηρίζει
/// το <see cref="AudioFileReader"/> (mp3/wav/aiff, και wma/m4a μέσω Media Foundation στα Windows).
/// Όλα σε try/catch ώστε να υποβαθμίζεται σιωπηλά αν δεν υπάρχει audio device / codec.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    private IWavePlayer? _output;
    private AudioFileReader? _reader;
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
            _reader = new AudioFileReader(path);
            _output = new WaveOutEvent();
            _output.Init(new LoopStream(_reader));
            ApplyVolume();
            _output.Play();
            _currentPath = path;
        }
        catch
        {
            Stop(); // λείπει codec/device → σιωπηλά χωρίς μουσική
        }
    }

    public void Stop()
    {
        try { _output?.Stop(); } catch { /* ignore */ }
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
        _currentPath = null;
    }

    private void ApplyVolume()
    {
        if (_reader is null) return;
        try { _reader.Volume = _muted ? 0f : _volume; } catch { /* ignore */ }
    }

    public void Dispose() => Stop();

    /// <summary>Τυλίγει ένα <see cref="WaveStream"/> ώστε να επαναλαμβάνεται ατέρμονα.</summary>
    private sealed class LoopStream : WaveStream
    {
        private readonly WaveStream _source;
        public LoopStream(WaveStream source) => _source = source;

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position { get => _source.Position; set => _source.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (_source.Position == 0) break; // κενό αρχείο → αποφυγή ατέρμονου βρόχου
                    _source.Position = 0;             // φτάσαμε στο τέλος → από την αρχή
                }
                total += read;
            }
            return total;
        }
    }
}
