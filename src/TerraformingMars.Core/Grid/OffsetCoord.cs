namespace TerraformingMars.Core.Grid;

/// <summary>
/// Offset coordinates (Col, Row) με σύμβαση <b>odd-r</b> (pointy-top: οι περιττές γραμμές
/// μετατοπίζονται δεξιά). Βολικό για ορθογώνιο χάρτη, για αποθήκευση σε πίνακα,
/// και για δειγματοληψία θορύβου. Οι μετατροπές προς/από <see cref="Hex"/> είναι ακριβείς
/// αντίστροφες για Row >= 0 (όπως πάντα στον χάρτη μας).
/// </summary>
public readonly struct OffsetCoord : IEquatable<OffsetCoord>
{
    public int Col { get; }
    public int Row { get; }

    public OffsetCoord(int col, int row)
    {
        Col = col;
        Row = row;
    }

    public static OffsetCoord FromHex(Hex h)
    {
        int col = h.Q + (h.R - (h.R & 1)) / 2;
        int row = h.R;
        return new OffsetCoord(col, row);
    }

    public Hex ToHex()
    {
        int q = Col - (Row - (Row & 1)) / 2;
        int r = Row;
        return new Hex(q, r);
    }

    public bool Equals(OffsetCoord other) => Col == other.Col && Row == other.Row;
    public override bool Equals(object? obj) => obj is OffsetCoord o && Equals(o);
    public override int GetHashCode() => HashCode.Combine(Col, Row);
    public override string ToString() => $"Offset({Col}, {Row})";
}
