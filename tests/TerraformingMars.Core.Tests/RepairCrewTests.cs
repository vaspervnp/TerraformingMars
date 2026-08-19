using System.Linq;
using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

/// <summary>
/// Συνεργείο επισκευής: ένας άποικος (ιδανικά Engineer) στέλνεται σε χαλασμένο κτήριο — ακόμη κι αν
/// είναι αυτόματο — επιταχύνει την επισκευή και αποδεσμεύεται μόνος του όταν αυτή τελειώσει.
/// </summary>
public class RepairCrewTests
{
    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static Building Broken(string id, int q, int r, int repairTicks)
    {
        var b = new Building(Catalog.Get(id), new Hex(q, r), startOperational: true);
        b.State = BuildingState.Disabled;
        b.RepairTicksRemaining = repairTicks;
        return b;
    }

    private static World WorldWith(Colony colony)
    {
        var map = new Generation.MapGenerator(new Generation.MapGenerationSettings { Width = 12, Height = 12, Seed = 3 }).Generate();
        var sponsor = SponsorCatalog.LoadDefault().Get("normal");
        return new World(map, colony, new[] { new EventSystem(sponsor, map.Seed) });
    }

    [Fact]
    public void Repair_Crew_Works_On_Automatic_Buildings_Too()
    {
        var colony = new Colony();
        var solar = Broken("solar_panel", 0, 0, 40);       // MaxWorkers = 0
        colony.AddBuilding(solar);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);

        Assert.False(colony.Assign(engineer, solar));       // κανονική ανάθεση: αδύνατη
        Assert.True(colony.AssignRepair(engineer, solar));  // συνεργείο: επιτρέπεται

        Assert.Same(solar, engineer.Assignment);
        Assert.Same(engineer, solar.RepairCrew);
        Assert.Empty(solar.Workers);
        Assert.DoesNotContain(engineer, colony.IdleColonists);
    }

    [Fact]
    public void Repair_Crew_Only_Goes_To_Broken_Buildings()
    {
        var colony = new Colony();
        var working = new Building(Catalog.Get("solar_panel"), new Hex(0, 0), startOperational: true);
        colony.AddBuilding(working);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);

        Assert.False(colony.AssignRepair(engineer, working));
        Assert.Null(engineer.Assignment);
    }

    [Fact]
    public void Engineer_Repairs_Three_Times_Faster_Than_Nobody()
    {
        var colony = new Colony();
        var alone = Broken("solar_panel", 0, 0, 100);
        var crewed = Broken("solar_panel", 1, 0, 100);
        colony.AddBuilding(alone);
        colony.AddBuilding(crewed);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.AssignRepair(engineer, crewed);

        var world = WorldWith(colony);
        for (int i = 0; i < 10; i++) world.Tick();

        Assert.Equal(90, alone.RepairTicksRemaining);      // 1/tick
        Assert.Equal(70, crewed.RepairTicksRemaining);     // 3/tick
    }

    [Fact]
    public void Non_Engineer_Repairs_Twice_As_Fast()
    {
        var colony = new Colony();
        var building = Broken("solar_panel", 0, 0, 100);
        colony.AddBuilding(building);
        var botanist = new Colonist("Chen", Specialty.Botanist);
        colony.Colonists.Add(botanist);
        colony.AssignRepair(botanist, building);

        var world = WorldWith(colony);
        for (int i = 0; i < 10; i++) world.Tick();

        Assert.Equal(80, building.RepairTicksRemaining);
    }

    [Fact]
    public void Crew_Is_Freed_When_The_Repair_Finishes()
    {
        var colony = new Colony();
        var building = Broken("solar_panel", 0, 0, 6);
        colony.AddBuilding(building);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.AssignRepair(engineer, building);

        var world = WorldWith(colony);
        for (int i = 0; i < 3; i++) world.Tick();

        Assert.Equal(BuildingState.Operational, building.State);
        Assert.Null(building.RepairCrew);
        Assert.Null(engineer.Assignment);
        Assert.Contains(engineer, colony.IdleColonists);    // ξανά στη δεξαμενή διαθέσιμων
    }

    [Fact]
    public void Sending_A_Second_Colonist_Frees_The_First()
    {
        var colony = new Colony();
        var building = Broken("iron_mine", 0, 0, 50);
        colony.AddBuilding(building);
        var first = new Colonist("Chen", Specialty.Botanist);
        var second = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.AddRange(new[] { first, second });

        colony.AssignRepair(first, building);
        colony.AssignRepair(second, building);

        Assert.Same(second, building.RepairCrew);
        Assert.Null(first.Assignment);
        Assert.Contains(first, colony.IdleColonists);
    }

    [Fact]
    public void Moving_The_Repair_Crew_To_A_Job_Clears_The_Repair_Slot()
    {
        var colony = new Colony();
        var broken = Broken("solar_panel", 0, 0, 50);
        var lab = new Building(Catalog.Get("research_lab"), new Hex(1, 0), startOperational: true);
        colony.AddBuilding(broken);
        colony.AddBuilding(lab);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.AssignRepair(engineer, broken);

        Assert.True(colony.Assign(engineer, lab));

        Assert.Null(broken.RepairCrew);
        Assert.Same(lab, engineer.Assignment);
        Assert.Equal(new[] { engineer }, lab.Workers);
    }

    [Fact]
    public void Unassigning_The_Repair_Crew_Sends_Them_Back_To_The_Pool()
    {
        var colony = new Colony();
        var building = Broken("solar_panel", 0, 0, 50);
        colony.AddBuilding(building);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(engineer);
        colony.AssignRepair(engineer, building);

        Assert.True(colony.Unassign(engineer));

        Assert.Null(building.RepairCrew);
        Assert.Null(engineer.Assignment);
    }
}
