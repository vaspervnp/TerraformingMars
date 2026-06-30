using TerraformingMars.Core.Grid;

namespace TerraformingMars.Core.Map;

/// <summary>
/// Ο πλανητικός χάρτης: συλλογή <see cref="HexTile"/> με κλειδί το <see cref="Hex"/>.
/// Dictionary-based ώστε queries γειτονιάς να είναι O(1) και να υποστηρίζονται
/// μη-ορθογώνια σχήματα στο μέλλον.
/// </summary>
public sealed class HexMap
{
    private readonly Dictionary<Hex, HexTile> _tiles;

    public int Width { get; }
    public int Height { get; }
    public int Seed { get; }

    public HexMap(int width, int height, int seed)
    {
        Width = width;
        Height = height;
        Seed = seed;
        _tiles = new Dictionary<Hex, HexTile>(width * height);
    }

    public int Count => _tiles.Count;
    public IReadOnlyCollection<HexTile> Tiles => _tiles.Values;

    internal void Add(HexTile tile) => _tiles[tile.Coord] = tile;

    public bool TryGetTile(Hex h, out HexTile? tile) => _tiles.TryGetValue(h, out tile);

    public HexTile? GetTile(Hex h) => _tiles.TryGetValue(h, out var t) ? t : null;

    public bool Contains(Hex h) => _tiles.ContainsKey(h);

    /// <summary>Οι υπαρκτοί γείτονες ενός hex (αγνοεί όσους πέφτουν εκτός χάρτη).</summary>
    public IEnumerable<HexTile> NeighborsOf(Hex h)
    {
        foreach (var n in h.Neighbors())
            if (_tiles.TryGetValue(n, out var t))
                yield return t;
    }
}
