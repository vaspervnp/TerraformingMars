using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Generation;

/// <summary>
/// Παράγει ντετερμινιστικά έναν <see cref="HexMap"/> από <see cref="MapGenerationSettings"/>.
/// <para>
/// Pass 1: δειγματοληψία υψομέτρου (χωρίς RNG) για όλα τα κελιά.<br/>
/// Pass 2: ταξινόμηση εδάφους με <b>quantile thresholds</b> (εγγυημένη ποικιλία) και
/// κατανομή πόρων (RNG σε σταθερή σειρά row-major ⇒ ίδιο seed = ίδιος χάρτης).
/// </para>
/// </summary>
public sealed class MapGenerator
{
    private readonly MapGenerationSettings _s;
    private readonly INoiseSource _elevation;
    private readonly INoiseSource _resourceField;
    private readonly Random _rng;

    public MapGenerator(MapGenerationSettings settings, INoiseSource? elevationNoise = null)
    {
        _s = settings;
        _rng = new Random(settings.Seed);
        _elevation = elevationNoise ?? new PerlinNoise(settings.Seed);
        _resourceField = new PerlinNoise(settings.Seed * 31 + 7);
    }

    public HexMap Generate()
    {
        var map = new HexMap(_s.Width, _s.Height, _s.Seed);
        int n = _s.Width * _s.Height;

        var cols = new int[n];
        var rows = new int[n];
        var hexes = new Hex[n];
        var elev = new float[n];
        var lat = new float[n];
        var nonPolar = new List<float>(n);

        // --- Pass 1: υψόμετρο (καθαρό noise, χωρίς RNG) ---
        int idx = 0;
        for (int row = 0; row < _s.Height; row++)
        {
            for (int col = 0; col < _s.Width; col++)
            {
                cols[idx] = col;
                rows[idx] = row;
                hexes[idx] = new OffsetCoord(col, row).ToHex();
                elev[idx] = SampleElevation(col, row);
                lat[idx] = Latitude(row);

                if (lat[idx] < _s.PolarLatitude)
                    nonPolar.Add(elev[idx]);

                idx++;
            }
        }

        // Κατώφλια από την πραγματική κατανομή υψομέτρου (εκτός πόλων).
        nonPolar.Sort();
        float canyonCut = Quantile(nonPolar, _s.CanyonQuantile);
        float lowlandCut = Quantile(nonPolar, _s.LowlandQuantile);
        float flatlandCut = Quantile(nonPolar, _s.FlatlandQuantile);
        float highlandCut = Quantile(nonPolar, _s.HighlandQuantile);

        // --- Pass 2: ταξινόμηση + πόροι (RNG, σταθερή σειρά) ---
        for (int i = 0; i < n; i++)
        {
            TerrainType terrain = lat[i] >= _s.PolarLatitude
                ? TerrainType.PolarIce
                : Classify(elev[i], canyonCut, lowlandCut, flatlandCut, highlandCut);

            ResourceDeposit deposit = RollResource(terrain, cols[i], rows[i]);
            map.Add(new HexTile(hexes[i], elev[i], terrain, deposit));
        }

        return map;
    }

    private float SampleElevation(int col, int row)
    {
        float nx = col * _s.NoiseScale;
        float ny = row * _s.NoiseScale;
        return _elevation.Fractal(nx, ny, _s.Octaves, _s.Lacunarity, _s.Persistence);
    }

    /// <summary>0 = ισημερινός, 1 = πόλος.</summary>
    private float Latitude(int row)
    {
        float half = _s.Height / 2f;
        return Math.Abs(row - half) / half;
    }

    private static float Quantile(List<float> sorted, float q)
    {
        if (sorted.Count == 0) return 0f;
        int i = (int)(q * (sorted.Count - 1));
        return sorted[Math.Clamp(i, 0, sorted.Count - 1)];
    }

    private TerrainType Classify(float e, float canyon, float lowland, float flatland, float highland)
    {
        if (e <= canyon) return TerrainType.Canyon;
        if (e <= lowland) return TerrainType.Lowland;
        if (e <= flatland) return Chance(_s.CraterChance) ? TerrainType.Crater : TerrainType.Flatland;
        if (e <= highland) return TerrainType.Highland;
        return TerrainType.Mountain;
    }

    private ResourceDeposit RollResource(TerrainType terrain, int col, int row)
    {
        // Το resource field δίνει συνεκτικές «φλέβες» αντί για ομοιόμορφο θόρυβο.
        float field = _resourceField.Fractal(col * _s.NoiseScale * 1.7f, row * _s.NoiseScale * 1.7f, 3);
        float vein = (field + 1f) * 0.5f; // [0, 1]

        ResourceType type = ResourceType.None;
        int amount = 0;

        switch (terrain)
        {
            case TerrainType.PolarIce:
                type = ResourceType.Ice;
                amount = _rng.Next(800, 1600);
                break;

            case TerrainType.Crater:
            case TerrainType.Canyon:
                if (Chance(_s.IceChance + 0.15f)) { type = ResourceType.Ice; amount = _rng.Next(200, 600); }
                break;

            case TerrainType.Mountain:
            case TerrainType.Highland:
                if (vein > 0.55f && Chance(_s.IronChance)) { type = ResourceType.Iron; amount = _rng.Next(300, 900); }
                else if (Chance(_s.SiliconChance)) { type = ResourceType.Silicon; amount = _rng.Next(200, 700); }
                break;

            case TerrainType.Flatland:
            case TerrainType.Lowland:
                if (Chance(_s.RegolithChance)) { type = ResourceType.Regolith; amount = _rng.Next(150, 500); }
                else if (Chance(_s.IceChance)) { type = ResourceType.Ice; amount = _rng.Next(100, 400); }
                break;
        }

        if (type == ResourceType.None) return ResourceDeposit.Empty;

        // Ο επιφανειακός πάγος είναι ορατός· τα μεταλλεύματα συχνά κρυμμένα (θέλουν Γεωλόγο).
        bool hidden = type != ResourceType.Ice && Chance(_s.HiddenDepositChance);
        return new ResourceDeposit(type, amount, hidden);
    }

    private bool Chance(float p) => _rng.NextDouble() < p;
}
