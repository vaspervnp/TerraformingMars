namespace TerraformingMars.Core.Grid;

/// <summary>
/// Μετατροπή hex ↔ 2D pixel χώρου για <b>pointy-top</b> εξάγωνα.
/// Καθαρά μαθηματικά — καμία εξάρτηση από engine (επιστρέφει tuples (x, y)).
/// Το <see cref="PixelToHex"/> χρησιμεύει για mouse picking στη Φάση 1.
/// </summary>
public sealed class HexLayout
{
    private static readonly double Sqrt3 = Math.Sqrt(3.0);

    public double SizeX { get; }
    public double SizeY { get; }
    public double OriginX { get; }
    public double OriginY { get; }

    public HexLayout(double size, double originX = 0, double originY = 0)
        : this(size, size, originX, originY) { }

    public HexLayout(double sizeX, double sizeY, double originX, double originY)
    {
        SizeX = sizeX;
        SizeY = sizeY;
        OriginX = originX;
        OriginY = originY;
    }

    public (double x, double y) HexToPixel(Hex h)
    {
        double x = SizeX * (Sqrt3 * h.Q + Sqrt3 / 2.0 * h.R);
        double y = SizeY * (3.0 / 2.0 * h.R);
        return (x + OriginX, y + OriginY);
    }

    public Hex PixelToHex(double px, double py)
    {
        double x = (px - OriginX) / SizeX;
        double y = (py - OriginY) / SizeY;
        double q = (Sqrt3 / 3.0 * x) - (1.0 / 3.0 * y);
        double r = (2.0 / 3.0) * y;
        return Round(q, r);
    }

    /// <summary>Στρογγυλοποίηση fractional cube coords στο πλησιέστερο έγκυρο hex.</summary>
    private static Hex Round(double q, double r)
    {
        double s = -q - r;
        int rq = (int)Math.Round(q);
        int rr = (int)Math.Round(r);
        int rs = (int)Math.Round(s);

        double dq = Math.Abs(rq - q);
        double dr = Math.Abs(rr - r);
        double ds = Math.Abs(rs - s);

        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        // αλλιώς το rs διορθώνεται έμμεσα· Q,R είναι ήδη συνεπή.

        return new Hex(rq, rr);
    }
}
