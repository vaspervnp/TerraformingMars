using System.Linq;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class HousingTests
{
    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 20, Height = 20, Seed = seed }).Generate();

    /// <summary>Buildable tile με ≥1 buildable γειτονικό — κατάλληλο για άγκυρα κάψουλας.</summary>
    private static HexTile AnchorWithNeighbour(HexMap map) =>
        map.Tiles.First(t => t.IsBuildable && t.Coord.Neighbors().Any(n => map.GetTile(n) is { IsBuildable: true }));

    private static Hex FreeBuildableNeighbour(HexMap map, Colony colony, Hex around) =>
        around.Neighbors().First(n => map.GetTile(n) is { IsBuildable: true } && !colony.IsOccupied(n));

    [Theory]
    [InlineData("easy", 14)]
    [InlineData("normal", 12)]
    [InlineData("hard", 10)]
    public void Sponsor_Difficulty_Sets_Starting_Housing(string sponsorId, int expected)
    {
        var world = ColonyFactory.CreateStartingWorld(Map(), sponsor: SponsorCatalog.LoadDefault().Get(sponsorId));
        // Στην αρχή κανένα κτίριο δεν προσθέτει HousingCapacity → η στέγαση ισούται με τη βάση του χορηγού.
        Assert.Equal(expected, world.Colony.Housing);
    }

    [Fact]
    public void Habitat_Module_Adds_Housing_Capacity()
    {
        var map = Map();
        var colony = new Colony { BaseHousing = 12 };
        var anchor = AnchorWithNeighbour(map);
        colony.AddBuilding(new Building(Catalog.Get("landing_capsule"), anchor.Coord, startOperational: true));
        Assert.Equal(12, colony.Housing);

        Hex adj = FreeBuildableNeighbour(map, colony, anchor.Coord);
        colony.AddBuilding(new Building(Catalog.Get("habitat_module"), adj, startOperational: true));
        Assert.Equal(18, colony.Housing); // +6 ανά habitat module
    }

    [Fact]
    public void Habitat_Module_Must_Connect_To_A_Habitat()
    {
        var map = Map();
        var colony = new Colony { BaseHousing = 12 };
        colony.Ledger.Set(ResourceKind.Credits, 100_000);

        var anchor = AnchorWithNeighbour(map);
        colony.AddBuilding(new Building(Catalog.Get("landing_capsule"), anchor.Coord, startOperational: true));
        var habDef = Catalog.Get("habitat_module");

        // Δίπλα στην κάψουλα → επιτρέπεται.
        Hex adj = FreeBuildableNeighbour(map, colony, anchor.Coord);
        Assert.True(colony.CanPlace(habDef, adj, map).Success);

        // Μακριά από κάθε κατοικία → απορρίπτεται (πρέπει να συνδέεται με το δίκτυο).
        var far = map.Tiles.First(t => t.IsBuildable && !colony.IsOccupied(t.Coord) && !colony.HasAdjacentHabitat(t.Coord));
        Assert.False(colony.CanPlace(habDef, far.Coord, map).Success);
    }

    [Fact]
    public void Habitat_Module_Chains_Off_Another_Module()
    {
        var map = Map(seed: 12);
        var colony = new Colony { BaseHousing = 12 };
        colony.Ledger.Set(ResourceKind.Credits, 100_000);

        var anchor = AnchorWithNeighbour(map);
        colony.AddBuilding(new Building(Catalog.Get("landing_capsule"), anchor.Coord, startOperational: true));

        Hex module1 = FreeBuildableNeighbour(map, colony, anchor.Coord);
        colony.AddBuilding(new Building(Catalog.Get("habitat_module"), module1, startOperational: true));

        // Ένα hex δίπλα στο module1 (δικτυωμένη κατοικία) αλλά όχι στην ίδια την κάψουλα.
        var chained = module1.Neighbors()
            .Where(n => map.GetTile(n) is { IsBuildable: true } && !colony.IsOccupied(n) && n != anchor.Coord)
            .ToList();
        Assert.NotEmpty(chained);
        Assert.True(colony.CanPlace(Catalog.Get("habitat_module"), chained[0], map).Success);
    }
}
