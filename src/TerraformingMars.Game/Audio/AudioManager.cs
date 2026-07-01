using System;
using Microsoft.Xna.Framework.Audio;

namespace TerraformingMars.Game.Audio;

/// <summary>
/// Παράγει απλά ηχητικά εφέ <b>προγραμματιστικά</b> (συνθεμένα PCM tones) — δεν χρειάζονται
/// αρχεία ήχου. Όλα τυλιγμένα σε try/catch ώστε να υποβαθμίζεται ομαλά (χωρίς ήχο) αν δεν
/// υπάρχει audio device.
/// </summary>
public sealed class AudioManager
{
    private const int SampleRate = 44100;

    private SoundEffect? _blip, _chime, _alert, _win, _lose;
    public bool Enabled { get; set; } = true;

    /// <summary>Ένταση εφέ 0..1.</summary>
    public float Volume { get; set; } = 0.8f;

    public AudioManager()
    {
        try
        {
            _blip = Tone(new[] { 880f }, 0.07f, 0.30f);
            _chime = Tone(new[] { 1175f }, 0.16f, 0.30f);
            _alert = Tone(new[] { 320f }, 0.20f, 0.35f);
            _win = Tone(new[] { 523f, 659f, 784f, 1047f }, 0.15f, 0.30f);
            _lose = Tone(new[] { 392f, 311f, 247f }, 0.25f, 0.35f);
        }
        catch
        {
            Enabled = false; // χωρίς audio device → σιωπηλά
        }
    }

    public void Blip() => Play(_blip);
    public void Chime() => Play(_chime);
    public void Alert() => Play(_alert);
    public void Win() => Play(_win);
    public void Lose() => Play(_lose);

    private void Play(SoundEffect? sound)
    {
        if (!Enabled || sound is null) return;
        try { sound.Play(Math.Clamp(Volume, 0f, 1f), 0f, 0f); } catch { /* ignore audio errors */ }
    }

    /// <summary>Συνθέτει μια ακολουθία νοτών (sine με γραμμικό envelope) σε ένα SoundEffect.</summary>
    private static SoundEffect Tone(float[] frequencies, float secondsPerNote, float volume)
    {
        int perNote = (int)(secondsPerNote * SampleRate);
        int total = perNote * frequencies.Length;
        var buffer = new byte[total * 2];

        int index = 0;
        foreach (float freq in frequencies)
        {
            for (int i = 0; i < perNote; i++)
            {
                double t = (double)i / SampleRate;
                double env = Envelope(i, perNote);
                double sample = Math.Sin(2 * Math.PI * freq * t) * env * volume;
                short s = (short)(sample * short.MaxValue);
                buffer[index++] = (byte)(s & 0xff);
                buffer[index++] = (byte)((s >> 8) & 0xff);
            }
        }

        return new SoundEffect(buffer, SampleRate, AudioChannels.Mono);
    }

    private static double Envelope(int i, int length)
    {
        double attack = length * 0.1;
        if (i < attack) return i / attack;                 // fade in
        return 1.0 - (i - attack) / (length - attack);     // fade out
    }
}
