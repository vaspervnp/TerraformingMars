namespace TerraformingMars.Core.Generation;

/// <summary>
/// Αφαίρεση πάνω από ένα 2D πεδίο θορύβου. Default υλοποίηση: <see cref="PerlinNoise"/>.
/// <para>
/// Όταν θελήσουμε Simplex / Cellular / domain-warp, προσθέτουμε το FastNoiseLite.cs
/// και έναν adapter <c>FastNoiseLiteSource : INoiseSource</c> — χωρίς καμία αλλαγή
/// στον <see cref="MapGenerator"/>.
/// </para>
/// </summary>
public interface INoiseSource
{
    /// <summary>Δειγματοληψία στο (x, y). Τιμή περίπου στο [-1, 1].</summary>
    float Sample(float x, float y);
}
