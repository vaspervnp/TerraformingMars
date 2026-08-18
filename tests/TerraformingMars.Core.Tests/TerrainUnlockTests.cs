using System.Linq;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Research;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

/// <summary>
/// Τεχνολογίες που ανοίγουν «κλειστά» terrain: θεμελιώσεις βουνού (κάθε κτίριο σε Mountain) και
/// πλωτές εξέδρες γεώτρησης (ice drill πάνω σε νερό που κρύβει ακόμη πάγο).
/// </summary>
public class TerrainUnlockTests
{
    private static readonly BuildingCatalog Buildings = BuildingCatalog.LoadDefault();

    private static HexMap Map(int seed = 11) =>
        new MapGenerator(new MapGenerationSettings { Width = 24, Height = 24, Seed = seed }).Generate();

    private static Colony RichColony()
    {
        var c = new Colony();
        c.Ledger.Set(ResourceKind.Energy, 100_000);
        c.Ledger.Set(ResourceKind.Materials, 1000);
        c.Ledger.Set(ResourceKind.Credits, 100_000);
        return c;
    }

    /// <summary>Ερευνά μια τεχνολογία (μαζί με τα prerequisites της) χωρίς να περιμένει research points.</summary>
    private static void Research(Colony colony, string techId)
    {
        var catalog = colony.Tech.Catalog;
        void Visit(string id)
        {
            var tech = catalog.Get(id);
            foreach (var prereq in tech.Prerequisites) Visit(prereq);
            colony.Tech.Restore(colony.Tech.Researched.Append(id).ToList(), null, 0, colony.Tech.Phase2Unlocked);
        }
        Visit(techId);
    }

    // ---------------------------------------------------------------- βουνά

    [Fact]
    public void Mountains_Are_Locked_Until_Mountain_Foundations()
    {
        var map = Map();
        var colony = RichColony();
        Research(colony, "heavy_metallurgy");
        var def = Buildings.Get("iron_mine");
        var mountain = map.Tiles.First(t => t.Terrain == TerrainType.Mountain && t.Deposit.Type == ResourceType.Iron);

        var before = colony.CanPlace(def, mountain.Coord, map);
        Assert.False(before.Success);
        Assert.Contains("Mountain Foundations", before.Error);   // το μήνυμα λέει τι λείπει

        Research(colony, "mountain_foundations");

        Assert.True(colony.TryPlaceBuilding(def, mountain.Coord, map).Success);
    }

    [Fact]
    public void Mountain_Foundations_Opens_Mountains_For_Any_Building()
    {
        var map = Map();
        var colony = RichColony();
        Research(colony, "mountain_foundations");
        var mountain = map.Tiles.First(t => t.Terrain == TerrainType.Mountain);

        Assert.True(colony.CanPlace(Buildings.Get("solar_panel"), mountain.Coord, map).Success);
    }

    [Fact]
    public void Mountain_Foundations_Needs_Heavy_Metallurgy_First()
    {
        var tree = new TechTree();
        var tech = tree.Catalog.Get("mountain_foundations");

        Assert.Contains("heavy_metallurgy", tech.Prerequisites);
        Assert.False(tree.CanResearch(tech));

        tree.Restore(new[] { "heavy_metallurgy" }, null, 0);
        Assert.True(tree.CanResearch(tech));
    }

    // ---------------------------------------------------------------- νερό + πάγος

    [Fact]
    public void Ice_Drill_On_Water_Needs_Offshore_Drilling()
    {
        var map = Map();
        var colony = RichColony();
        // Λιωμένος πάγος: το terrain έγινε Water αλλά το κοίτασμα πάγου μένει από κάτω.
        var melted = map.Tiles.First(t => t.Terrain == TerrainType.PolarIce && t.Deposit.Type == ResourceType.Ice);
        melted.Terrain = TerrainType.Water;
        var drill = Buildings.Get("ice_drill");

        var before = colony.CanPlace(drill, melted.Coord, map);
        Assert.False(before.Success);
        Assert.Contains("Offshore Drill Platforms", before.Error);

        Research(colony, "offshore_drilling");

        Assert.True(colony.TryPlaceBuilding(drill, melted.Coord, map).Success);
    }

    [Fact]
    public void Offshore_Drilling_Only_Opens_Water_For_Ice_Miners()
    {
        var map = Map();
        var colony = RichColony();
        Research(colony, "offshore_drilling");
        var water = map.Tiles.First(t => t.Terrain == TerrainType.PolarIce && t.Deposit.Type == ResourceType.Ice);
        water.Terrain = TerrainType.Water;

        // Ένα solar panel δεν επιπλέει: το ξεκλείδωμα ισχύει μόνο για κτίρια που εξορύσσουν πάγο.
        Assert.False(colony.CanPlace(Buildings.Get("solar_panel"), water.Coord, map).Success);
        Assert.True(colony.CanPlace(Buildings.Get("ice_drill"), water.Coord, map).Success);
    }

    [Fact]
    public void Offshore_Drilling_Does_Not_Help_On_Water_Without_Ice()
    {
        var map = Map();
        var colony = RichColony();
        Research(colony, "offshore_drilling");
        var water = map.Tiles.First(t => t.Terrain == TerrainType.Flatland && t.Deposit.Type == ResourceType.None);
        water.Terrain = TerrainType.Water;

        var result = colony.CanPlace(Buildings.Get("ice_drill"), water.Coord, map);

        Assert.False(result.Success);   // το terrain ανοίγει, αλλά λείπει το κοίτασμα πάγου
        Assert.Contains("Ice deposit", result.Error);
    }

    [Fact]
    public void Offshore_Drilling_Needs_Orbital_Mirrors_First()
    {
        var tree = new TechTree();
        var tech = tree.Catalog.Get("offshore_drilling");

        Assert.Contains("orbital_mirrors", tech.Prerequisites);
        Assert.False(tree.CanResearch(tech));

        tree.Restore(new[] { "orbital_mirrors" }, null, 0);
        Assert.True(tree.CanResearch(tech));
    }
}
