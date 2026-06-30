using TerraformingMars.Core.Grid;

namespace TerraformingMars.Core.Map;

/// <summary>
/// Ένα κελί του χάρτη: θέση + στατικά γεωλογικά δεδομένα + runtime gameplay state.
/// Το <see cref="Terrain"/> και το <see cref="Deposit"/> αλλάζουν μόνο εσωτερικά
/// (π.χ. terraforming στη Φάση 5, εξάντληση κοιτάσματος στη Φάση 4).
/// </summary>
public sealed class HexTile
{
    public Hex Coord { get; }
    public float Elevation { get; }                       // κανονικοποιημένο ~[-1, 1]
    public TerrainType Terrain { get; internal set; }
    public ResourceDeposit Deposit { get; internal set; }

    // Runtime state (επόμενες φάσεις)
    public bool Discovered { get; set; }

    public bool IsBuildable => Terrain is not (TerrainType.Water or TerrainType.Mountain);

    public HexTile(Hex coord, float elevation, TerrainType terrain, ResourceDeposit deposit)
    {
        Coord = coord;
        Elevation = elevation;
        Terrain = terrain;
        Deposit = deposit;
    }

    public override string ToString() =>
        $"{Coord} {Terrain} elev={Elevation:0.00} {Deposit.Type}({Deposit.Amount})";
}
