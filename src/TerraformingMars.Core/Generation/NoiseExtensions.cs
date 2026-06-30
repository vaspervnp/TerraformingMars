namespace TerraformingMars.Core.Generation;

public static class NoiseExtensions
{
    /// <summary>
    /// Fractal Brownian Motion: αθροίζει πολλαπλά octaves θορύβου για φυσικό ανάγλυφο
    /// (μεγάλες μορφές + λεπτομέρεια). Το αποτέλεσμα κανονικοποιείται ξανά στο ~[-1, 1].
    /// </summary>
    public static float Fractal(this INoiseSource noise, float x, float y,
        int octaves = 4, float lacunarity = 2f, float persistence = 0.5f)
    {
        float amplitude = 1f;
        float frequency = 1f;
        float sum = 0f;
        float max = 0f;

        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * noise.Sample(x * frequency, y * frequency);
            max += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }

        return max > 0f ? sum / max : 0f;
    }
}
