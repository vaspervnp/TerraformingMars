using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Στήνει την αρχική αποικία: κάψουλα + βασικά κτίρια (operational), ορυχεία πάνω σε
/// κοιτάσματα, εργαστήριο έρευνας, και πλήρωμα 4 ειδικοτήτων ανατεθειμένο κατάλληλα.
/// </summary>
public static class ColonyFactory
{
    public static World CreateStartingWorld(HexMap map, BuildingCatalog? catalog = null,
        SponsorProfile? sponsor = null, bool enableEvents = false)
    {
        catalog ??= BuildingCatalog.LoadDefault();
        sponsor ??= SponsorCatalog.LoadDefault().Get("normal");
        var colony = new Colony { BaseHousing = sponsor.BaseHousing };
        var occupied = new HashSet<Hex>();

        Hex spot = FindLandingSpot(map);
        occupied.Add(spot);
        colony.AddBuilding(new Building(catalog.Get("landing_capsule"), spot, startOperational: true));

        double m = sponsor.StartingResourceMultiplier;
        var ledger = colony.Ledger;
        ledger.Set(ResourceKind.Energy, 2500 * m);
        ledger.Set(ResourceKind.Water, 800 * m);
        ledger.Set(ResourceKind.Oxygen, 800 * m);
        ledger.Set(ResourceKind.Food, 600 * m);
        ledger.Set(ResourceKind.Materials, 300 * m);
        ledger.Set(ResourceKind.Credits, 100_000 * m);

        // Μη-εξορυκτικά κτίρια στο δαχτυλίδι γύρω από την κάψουλα
        var ring = Spiral(spot, 4).Skip(1).GetEnumerator();
        void PlaceOnRing(string id)
        {
            while (ring.MoveNext())
            {
                Hex h = ring.Current;
                if (!occupied.Contains(h) && map.GetTile(h) is { IsBuildable: true })
                {
                    occupied.Add(h);
                    colony.AddBuilding(new Building(catalog.Get(id), h, startOperational: true));
                    return;
                }
            }
        }
        PlaceOnRing("solar_panel");
        PlaceOnRing("solar_panel");
        PlaceOnRing("o2_recycler");
        PlaceOnRing("hydroponics");
        PlaceOnRing("research_lab");

        // Ορυχεία πάνω στα πλησιέστερα κατάλληλα κοιτάσματα
        void PlaceOnDeposit(string id, ResourceType deposit)
        {
            if (FindNearestDeposit(map, spot, deposit, occupied) is { } hex)
            {
                occupied.Add(hex);
                colony.AddBuilding(new Building(catalog.Get(id), hex, startOperational: true));
            }
        }
        PlaceOnDeposit("ice_drill", ResourceType.Ice);
        PlaceOnDeposit("regolith_printer", ResourceType.Regolith);

        // Πλήρωμα + ανάθεση
        var engineer = new Colonist("Ada Reyes", Specialty.Engineer);
        var geologist = new Colonist("Boris Kane", Specialty.Geologist);
        var botanist = new Colonist("Chen Liu", Specialty.Botanist);
        var climatologist = new Colonist("Dara Okafor", Specialty.Climatologist);
        colony.Colonists.AddRange(new[] { engineer, geologist, botanist, climatologist });
        colony.Crew = colony.Colonists.Count;

        AssignToFirst(colony, engineer, "o2_recycler");
        AssignToFirst(colony, geologist, "ice_drill");
        AssignToFirst(colony, botanist, "hydroponics");
        AssignToFirst(colony, climatologist, "research_lab");

        var systems = new List<ISimulationSystem>();
        if (enableEvents) systems.Add(new EventSystem(sponsor, map.Seed));
        systems.Add(new ConstructionSystem());
        systems.Add(new ProductionSystem());
        systems.Add(new MarketSystem());
        systems.Add(new ResearchSystem());
        systems.Add(new PlanetSystem());
        systems.Add(new BiosphereSystem());
        systems.Add(new Phase2System());
        systems.Add(new PopulationSystem(map.Seed));
        systems.Add(new LifeSupportSystem());
        return new World(map, colony, systems);
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

    private static Hex? FindNearestDeposit(HexMap map, Hex from, ResourceType type, HashSet<Hex> occupied)
    {
        HexTile? best = null;
        int bestDist = int.MaxValue;

        foreach (var tile in map.Tiles)
        {
            if (!tile.IsBuildable || tile.Deposit.Type != type || occupied.Contains(tile.Coord)) continue;
            int d = tile.Coord.DistanceTo(from);
            if (d < bestDist) { bestDist = d; best = tile; }
        }

        return best?.Coord;
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
