using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Στήνει την αρχική αποικία: κάψουλα προσεδάφισης + βασικά κτίρια (ήδη operational)
/// + πλήρωμα 4 ειδικοτήτων ανατεθειμένο στα κατάλληλα κτίρια.
/// </summary>
public static class ColonyFactory
{
    public static World CreateStartingWorld(HexMap map, BuildingCatalog? catalog = null)
    {
        catalog ??= BuildingCatalog.LoadDefault();
        var colony = new Colony();

        Hex spot = FindLandingSpot(map);

        // Κάψουλα: δίνει αποθήκευση & «στεγάζει» το πλήρωμα (operational από την αρχή)
        colony.AddBuilding(new Building(catalog.Get("landing_capsule"), spot, startOperational: true));

        // Αρχικά αποθέματα (αφού η κάψουλα όρισε τις χωρητικότητες)
        var ledger = colony.Ledger;
        ledger.Set(ResourceKind.Energy, 2500);
        ledger.Set(ResourceKind.Water, 800);
        ledger.Set(ResourceKind.Oxygen, 800);
        ledger.Set(ResourceKind.Food, 600);
        ledger.Set(ResourceKind.Materials, 300);
        ledger.Set(ResourceKind.Credits, 100_000);

        // Βασικά κτίρια γύρω από την κάψουλα (prebuilt· τα starter παρακάμπτουν validation κοιτασμάτων)
        var spots = Spiral(spot, 4).Skip(1).GetEnumerator();
        void Place(string id)
        {
            while (spots.MoveNext())
            {
                Hex h = spots.Current;
                if (map.GetTile(h) is { IsBuildable: true } && !colony.IsOccupied(h))
                {
                    colony.AddBuilding(new Building(catalog.Get(id), h, startOperational: true));
                    return;
                }
            }
        }

        Place("solar_panel");
        Place("solar_panel");
        Place("o2_recycler");
        Place("ice_drill");
        Place("hydroponics");
        Place("regolith_printer");

        // Πλήρωμα + ανάθεση στις κατάλληλες θέσεις
        var engineer = new Colonist("Ada Reyes", Specialty.Engineer);
        var geologist = new Colonist("Boris Kane", Specialty.Geologist);
        var botanist = new Colonist("Chen Liu", Specialty.Botanist);
        var climatologist = new Colonist("Dara Okafor", Specialty.Climatologist);
        colony.Colonists.AddRange(new[] { engineer, geologist, botanist, climatologist });
        colony.Crew = colony.Colonists.Count;

        AssignToFirst(colony, engineer, "o2_recycler");
        AssignToFirst(colony, geologist, "ice_drill");
        AssignToFirst(colony, botanist, "hydroponics");
        AssignToFirst(colony, climatologist, "regolith_printer");

        return new World(map, colony, new ISimulationSystem[]
        {
            new ConstructionSystem(),
            new ProductionSystem(),
            new LifeSupportSystem()
        });
    }

    private static void AssignToFirst(Colony colony, Colonist colonist, string buildingId)
    {
        var building = colony.Buildings.FirstOrDefault(b => b.Definition.Id == buildingId);
        if (building is not null) colony.Assign(colonist, building);
    }

    private static Hex FindLandingSpot(HexMap map)
    {
        Hex center = new OffsetCoord(map.Width / 2, map.Height / 2).ToHex();
        HexTile? best = null;
        int bestDist = int.MaxValue;

        foreach (var tile in map.Tiles)
        {
            if (tile.Terrain != TerrainType.Flatland) continue;
            int d = tile.Coord.DistanceTo(center);
            if (d < bestDist) { bestDist = d; best = tile; }
        }

        return (best ?? map.Tiles.First()).Coord;
    }

    /// <summary>Hexes σε δαχτυλίδια γύρω από το κέντρο (από μέσα προς τα έξω).</summary>
    private static IEnumerable<Hex> Spiral(Hex center, int radius)
    {
        yield return center;
        for (int r = 1; r <= radius; r++)
        {
            Hex hex = center + Hex.Direction(4) * r;
            for (int side = 0; side < 6; side++)
                for (int step = 0; step < r; step++)
                {
                    yield return hex;
                    hex = hex.Neighbor(side);
                }
        }
    }
}
