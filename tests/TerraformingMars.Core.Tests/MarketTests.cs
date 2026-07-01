using System.Linq;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Map;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class MarketTests
{
    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static (World world, Colony colony) EmptyWorld(int seed = 7)
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 16, Height = 16, Seed = seed }).Generate();
        var colony = new Colony();
        var world = new World(map, colony, new List<ISimulationSystem>()); // ticks τρέχουν χειροκίνητα
        return (world, colony);
    }

    private static Building PlaceStaffed(Colony colony, string id, Hex hex, Specialty specialty)
    {
        var building = new Building(Catalog.Get(id), hex, startOperational: true);
        colony.AddBuilding(building);
        var worker = new Colonist("Tester", specialty);
        colony.Colonists.Add(worker);
        colony.Assign(worker, building);
        return building;
    }

    [Fact]
    public void ExportTerminal_Sells_Silicon_For_Credits()
    {
        var (world, colony) = EmptyWorld();
        var terminal = PlaceStaffed(colony, "export_terminal", new Hex(0, 0), Specialty.Engineer);
        colony.Ledger.Set(ResourceKind.Silicon, 100);
        colony.Ledger.Set(ResourceKind.Credits, 0);

        new MarketSystem().Tick(world);

        double expectedSold = Catalog.Get("export_terminal").ExportPerTick * terminal.WorkerEfficiency();
        Assert.True(expectedSold > 0);
        Assert.Equal(100 - expectedSold, colony.Ledger.Get(ResourceKind.Silicon), 6);
        Assert.Equal(expectedSold * MarketSystem.SiliconPrice, colony.Ledger.Get(ResourceKind.Credits), 6);
    }

    [Fact]
    public void ExportTerminal_Sells_Only_What_Is_In_Stock()
    {
        var (world, colony) = EmptyWorld();
        PlaceStaffed(colony, "export_terminal", new Hex(0, 0), Specialty.Engineer);
        colony.Ledger.Set(ResourceKind.Silicon, 2);
        colony.Ledger.Set(ResourceKind.Credits, 0);

        new MarketSystem().Tick(world); // ζήτηση (3 × eff) > απόθεμα (2) → πουλά μόνο 2

        Assert.Equal(0, colony.Ledger.Get(ResourceKind.Silicon), 6);
        Assert.Equal(2 * MarketSystem.SiliconPrice, colony.Ledger.Get(ResourceKind.Credits), 6);
    }

    [Fact]
    public void MarketSystem_Does_Not_Mint_Credits_Without_Silicon()
    {
        var (world, colony) = EmptyWorld();
        PlaceStaffed(colony, "export_terminal", new Hex(0, 0), Specialty.Engineer);
        colony.Ledger.Set(ResourceKind.Silicon, 0);
        colony.Ledger.Set(ResourceKind.Credits, 500);

        new MarketSystem().Tick(world);

        Assert.Equal(500, colony.Ledger.Get(ResourceKind.Credits), 6);
    }

    [Fact]
    public void ExportTerminal_Without_Worker_Sells_Nothing()
    {
        var (world, colony) = EmptyWorld();
        colony.AddBuilding(new Building(Catalog.Get("export_terminal"), new Hex(0, 0), startOperational: true));
        colony.Ledger.Set(ResourceKind.Silicon, 100);
        colony.Ledger.Set(ResourceKind.Credits, 0);

        new MarketSystem().Tick(world); // MaxWorkers=1, καμία ανάθεση → efficiency 0

        Assert.Equal(100, colony.Ledger.Get(ResourceKind.Silicon), 6);
        Assert.Equal(0, colony.Ledger.Get(ResourceKind.Credits), 6);
    }

    [Fact]
    public void SiliconMine_Extracts_Silicon_From_Deposit()
    {
        // Βρες παραγόμενο χάρτη με κοίτασμα Silicon.
        HexTile? siliconTile = null;
        World world = null!;
        Colony colony = null!;
        for (int seed = 1; seed < 80 && siliconTile is null; seed++)
        {
            var map = new MapGenerator(new MapGenerationSettings { Width = 20, Height = 20, Seed = seed }).Generate();
            var tile = map.Tiles.FirstOrDefault(t => t.Deposit.Type == ResourceType.Silicon && t.IsBuildable);
            if (tile is not null)
            {
                siliconTile = tile;
                colony = new Colony();
                world = new World(map, colony, new List<ISimulationSystem>());
            }
        }
        Assert.NotNull(siliconTile);

        colony.Ledger.Set(ResourceKind.Energy, 1000); // ώστε να μην στραγγαλιστεί από brownout
        var mine = PlaceStaffed(colony, "silicon_mine", siliconTile!.Coord, Specialty.Geologist);

        double depositBefore = siliconTile.RemainingDeposit;
        new ProductionSystem().Tick(world);

        Assert.True(colony.Ledger.Get(ResourceKind.Silicon) > 0);
        Assert.True(siliconTile.RemainingDeposit < depositBefore);
        Assert.True(mine.WorkerEfficiency() > 0);
    }

    [Fact]
    public void Mine_And_Terminal_Form_A_Silicon_To_Credits_Loop()
    {
        // Ολοκληρωμένος βρόχος: εξόρυξη Silicon → πώληση → Credits, μέσω World.Tick.
        HexTile? siliconTile = null;
        for (int seed = 1; seed < 80 && siliconTile is null; seed++)
        {
            var map = new MapGenerator(new MapGenerationSettings { Width = 20, Height = 20, Seed = seed }).Generate();
            var tile = map.Tiles.FirstOrDefault(t => t.Deposit.Type == ResourceType.Silicon && t.IsBuildable);
            if (tile is null) continue;

            var neighbour = map.GetTile(tile.Coord.Neighbor(0));
            if (neighbour is null || !neighbour.IsBuildable) continue;

            var colony = new Colony();
            colony.Ledger.Set(ResourceKind.Energy, 5000);
            colony.Ledger.Set(ResourceKind.Credits, 0);
            var world = new World(map, colony, new List<ISimulationSystem>
            {
                new ProductionSystem(), new MarketSystem()
            });

            PlaceStaffed(colony, "silicon_mine", tile.Coord, Specialty.Geologist);
            PlaceStaffed(colony, "export_terminal", neighbour.Coord, Specialty.Engineer);

            for (int i = 0; i < 50; i++) world.Tick();

            Assert.True(colony.Ledger.Get(ResourceKind.Credits) > 0, "ο βρόχος πρέπει να παράγει credits");
            siliconTile = tile; // βρέθηκε & δοκιμάστηκε
        }
        Assert.NotNull(siliconTile);
    }
}
