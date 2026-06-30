namespace TerraformingMars.Core.Grid;

/// <summary>
/// Άκαμπτη (immutable) θέση σε pointy-top hex grid, σε <b>axial coordinates</b> (Q, R).
/// Το τρίτο cube-coordinate S = -Q-R είναι παράγωγο, ώστε Q+R+S == 0 πάντα.
/// Όλη η γεωμετρία (απόσταση, γειτονιά) είναι τετριμμένη πάνω σε cube coords.
/// Ref: https://www.redblobgames.com/grids/hexagons/
/// </summary>
public readonly struct Hex : IEquatable<Hex>
{
    public int Q { get; }
    public int R { get; }
    public int S => -Q - R;

    public Hex(int q, int r)
    {
        Q = q;
        R = r;
    }

    public static readonly Hex Zero = new(0, 0);

    // Οι 6 κατευθύνσεις γειτόνων (axial), με σταθερή σειρά (0 = ανατολικά, δεξιόστροφα).
    private static readonly Hex[] Directions =
    {
        new(1, 0), new(1, -1), new(0, -1),
        new(-1, 0), new(-1, 1), new(0, 1),
    };

    public static Hex Direction(int index) => Directions[((index % 6) + 6) % 6];

    public Hex Neighbor(int direction) => this + Direction(direction);

    public IEnumerable<Hex> Neighbors()
    {
        foreach (var d in Directions)
            yield return this + d;
    }

    public static Hex operator +(Hex a, Hex b) => new(a.Q + b.Q, a.R + b.R);
    public static Hex operator -(Hex a, Hex b) => new(a.Q - b.Q, a.R - b.R);
    public static Hex operator *(Hex a, int k) => new(a.Q * k, a.R * k);

    /// <summary>Απόσταση από το (0,0) σε hex βήματα.</summary>
    public int Length() => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

    /// <summary>Πλήθος hex βημάτων μέχρι το <paramref name="other"/>.</summary>
    public int DistanceTo(Hex other) => (this - other).Length();

    public bool Equals(Hex other) => Q == other.Q && R == other.R;
    public override bool Equals(object? obj) => obj is Hex h && Equals(h);
    public override int GetHashCode() => HashCode.Combine(Q, R);
    public override string ToString() => $"Hex({Q}, {R}, {S})";

    public static bool operator ==(Hex a, Hex b) => a.Equals(b);
    public static bool operator !=(Hex a, Hex b) => !a.Equals(b);
}
