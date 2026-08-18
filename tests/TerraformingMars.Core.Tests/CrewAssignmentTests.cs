using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Grid;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

/// <summary>
/// Ανάθεση/ανταλλαγή αποίκων — η λογική πίσω από το drag &amp; drop της οθόνης «Crew assignments».
/// </summary>
public class CrewAssignmentTests
{
    private static readonly BuildingCatalog Catalog = BuildingCatalog.LoadDefault();

    private static Building Make(string id, int q, int r) =>
        new(Catalog.Get(id), new Hex(q, r), startOperational: true);

    [Fact]
    public void Swap_Exchanges_Buildings_Of_Two_Workers()
    {
        var colony = new Colony();
        var mine = Make("iron_mine", 0, 0);
        var lab = Make("research_lab", 1, 0);
        var geologist = new Colonist("Boris", Specialty.Geologist);
        var engineer = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.AddRange(new[] { geologist, engineer });
        colony.Assign(geologist, mine);
        colony.Assign(engineer, lab);

        Assert.True(colony.SwapAssignments(geologist, engineer));

        Assert.Same(lab, geologist.Assignment);
        Assert.Same(mine, engineer.Assignment);
        Assert.Equal(new[] { engineer }, mine.Workers);
        Assert.Equal(new[] { geologist }, lab.Workers);
    }

    [Fact]
    public void Swap_With_Idle_Colonist_Frees_The_Other()
    {
        var colony = new Colony();
        var mine = Make("iron_mine", 0, 0);
        var worker = new Colonist("Ada", Specialty.Engineer);
        var idle = new Colonist("Chen", Specialty.Botanist);
        colony.Colonists.AddRange(new[] { worker, idle });
        colony.Assign(worker, mine);

        Assert.True(colony.SwapAssignments(idle, worker));

        Assert.Same(mine, idle.Assignment);
        Assert.Null(worker.Assignment);
        Assert.Equal(new[] { idle }, mine.Workers);
    }

    [Fact]
    public void Swap_Is_NoOp_For_Same_Building_Or_Same_Colonist()
    {
        var colony = new Colony();
        var lab = Make("research_lab", 0, 0);
        var a = new Colonist("A", Specialty.Engineer);
        var b = new Colonist("B", Specialty.Engineer);
        colony.Colonists.AddRange(new[] { a, b });

        Assert.False(colony.SwapAssignments(a, a));
        Assert.False(colony.SwapAssignments(a, b)); // και οι δύο idle

        colony.Assign(a, lab);
        Assert.False(colony.SwapAssignments(a, a));
        Assert.Same(lab, a.Assignment);
    }

    [Fact]
    public void AssignOrSwap_Uses_Free_Slot_When_Available()
    {
        var colony = new Colony();
        var mine = Make("iron_mine", 0, 0);
        var colonist = new Colonist("Boris", Specialty.Geologist);
        colony.Colonists.Add(colonist);

        Assert.True(colony.AssignOrSwap(colonist, mine));

        Assert.Same(mine, colonist.Assignment);
        Assert.Equal(new[] { colonist }, mine.Workers);
    }

    [Fact]
    public void AssignOrSwap_Swaps_When_Target_Is_Full()
    {
        var colony = new Colony();
        var mine = Make("iron_mine", 0, 0);   // MaxWorkers = 1
        var lab = Make("research_lab", 1, 0);
        var occupant = new Colonist("Ada", Specialty.Engineer);
        var incoming = new Colonist("Boris", Specialty.Geologist);
        colony.Colonists.AddRange(new[] { occupant, incoming });
        colony.Assign(occupant, mine);
        colony.Assign(incoming, lab);

        Assert.True(colony.AssignOrSwap(incoming, mine));

        Assert.Same(mine, incoming.Assignment);
        Assert.Same(lab, occupant.Assignment);
    }

    [Fact]
    public void AssignOrSwap_Swaps_With_The_Requested_Worker_Even_If_Slots_Are_Free()
    {
        var colony = new Colony();
        var multi = Make("isolation_hospital", 0, 0); // MaxWorkers = 2
        var lab = Make("research_lab", 1, 0);
        var target = new Colonist("Ada", Specialty.Engineer);
        var incoming = new Colonist("Boris", Specialty.Geologist);
        colony.Colonists.AddRange(new[] { target, incoming });
        colony.Assign(target, multi);
        colony.Assign(incoming, lab);

        // Ρητό drop πάνω στη θέση του "target": ανταλλαγή, ακόμη κι αν το κτήριο έχει κενή θέση.
        Assert.True(colony.AssignOrSwap(incoming, multi, target));

        Assert.Same(multi, incoming.Assignment);
        Assert.Same(lab, target.Assignment);
    }

    [Fact]
    public void AssignOrSwap_Rejects_Buildings_Without_Jobs_And_Same_Building_Drops()
    {
        var colony = new Colony();
        var solar = Make("solar_panel", 0, 0); // MaxWorkers = 0
        var lab = Make("research_lab", 1, 0);
        var colonist = new Colonist("Ada", Specialty.Engineer);
        colony.Colonists.Add(colonist);

        Assert.False(colony.AssignOrSwap(colonist, solar));
        Assert.Null(colonist.Assignment);

        colony.Assign(colonist, lab);
        Assert.False(colony.AssignOrSwap(colonist, lab)); // drop στο ίδιο κτήριο = τίποτα
        Assert.Same(lab, colonist.Assignment);
    }
}
