using TerraformingMars.Core.Generation;
using TerraformingMars.Core.Simulation;
using Xunit;

namespace TerraformingMars.Core.Tests;

public class GameClockTests
{
    [Fact]
    public void Paused_Produces_No_Ticks()
    {
        var clock = new GameClock { Speed = GameSpeed.Paused };
        Assert.Equal(0, clock.Advance(10.0));
        Assert.Equal(0, clock.TotalTicks);
    }

    [Fact]
    public void Normal_Speed_Steps_Four_Ticks_Per_Second()
    {
        var clock = new GameClock { Speed = GameSpeed.Normal };
        Assert.Equal(4, clock.Advance(1.0));
    }

    [Fact]
    public void Accumulator_Carries_Fractional_Time()
    {
        var clock = new GameClock { Speed = GameSpeed.Normal }; // 0.25s ανά tick
        Assert.Equal(0, clock.Advance(0.2)); // 0.8 tick → 0, υπόλοιπο 0.8
        Assert.Equal(1, clock.Advance(0.1)); // +0.4 → 1.2 → 1 tick
    }

    [Fact]
    public void Faster_Speed_Produces_More_Ticks()
    {
        var normal = new GameClock { Speed = GameSpeed.Normal };
        var ultra = new GameClock { Speed = GameSpeed.Ultra };
        Assert.True(ultra.Advance(1.0) > normal.Advance(1.0));
    }

    [Fact]
    public void Sol_Counter_Advances_With_Time()
    {
        var clock = new GameClock { Speed = GameSpeed.Normal };
        Assert.Equal(1, clock.Sol);
        // 144 ticks ≈ 1 Sol· τρέξε αρκετό χρόνο
        for (int i = 0; i < 200; i++) clock.Advance(1.0);
        Assert.True(clock.Sol > 1);
    }
}

public class ResourceLedgerTests
{
    [Fact]
    public void Add_Respects_Capacity()
    {
        var ledger = new ResourceLedger();
        ledger.AddCapacity(ResourceKind.Energy, 100);
        double added = ledger.Add(ResourceKind.Energy, 150);
        Assert.Equal(100, ledger.Get(ResourceKind.Energy));
        Assert.Equal(100, added);
    }

    [Fact]
    public void Add_Never_Goes_Below_Zero()
    {
        var ledger = new ResourceLedger();
        ledger.Set(ResourceKind.Water, 10);
        ledger.Add(ResourceKind.Water, -50);
        Assert.Equal(0, ledger.Get(ResourceKind.Water));
    }

    [Fact]
    public void TryConsume_Fails_When_Insufficient()
    {
        var ledger = new ResourceLedger();
        ledger.Set(ResourceKind.Food, 10);
        Assert.False(ledger.TryConsume(ResourceKind.Food, 20));
        Assert.Equal(10, ledger.Get(ResourceKind.Food));
        Assert.True(ledger.TryConsume(ResourceKind.Food, 5));
        Assert.Equal(5, ledger.Get(ResourceKind.Food));
    }
}

public class WorldSimulationTests
{
    private static World StartingWorld(int seed = 7)
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 12, Height = 12, Seed = seed }).Generate();
        return ColonyFactory.CreateStartingWorld(map);
    }

    [Fact]
    public void Production_Accumulates_Food_Over_Ticks()
    {
        var world = StartingWorld();
        double start = world.Colony.Ledger.Get(ResourceKind.Food);
        for (int i = 0; i < 100; i++) world.Tick();
        Assert.True(world.Colony.Ledger.Get(ResourceKind.Food) > start);
        Assert.False(world.Colony.LifeSupportFailing);
    }

    [Fact]
    public void Energy_Caps_At_Battery_Capacity()
    {
        var world = StartingWorld();
        for (int i = 0; i < 5000; i++) world.Tick();
        Assert.Equal(world.Colony.Ledger.Capacity(ResourceKind.Energy),
                     world.Colony.Ledger.Get(ResourceKind.Energy));
    }

    [Fact]
    public void Net_Rate_Equals_Actual_Change_And_Is_Positive()
    {
        var world = StartingWorld();
        double before = world.Colony.Ledger.Get(ResourceKind.Energy);
        world.Tick();
        double after = world.Colony.Ledger.Get(ResourceKind.Energy);

        Assert.Equal(after - before, world.Colony.Ledger.RatePerTick(ResourceKind.Energy), 6);
        Assert.True(world.Colony.Ledger.RatePerTick(ResourceKind.Energy) > 0); // ηλιακά > κατανάλωση
    }

    [Fact]
    public void LifeSupport_Fails_Without_Supplies()
    {
        var map = new MapGenerator(new MapGenerationSettings { Width = 8, Height = 8, Seed = 3 }).Generate();
        var colony = new Colony { Crew = 4 }; // καθόλου αποθέματα/παραγωγή
        var world = new World(map, colony);

        world.Tick();
        Assert.True(colony.LifeSupportFailing);
    }

    [Fact]
    public void Update_Runs_Ticks_Based_On_Real_Time()
    {
        var world = StartingWorld();
        world.Clock.Speed = GameSpeed.Normal;
        int ticks = world.Update(1.0); // ~4 ticks/sec
        Assert.Equal(4, ticks);
        Assert.Equal(4, world.Clock.TotalTicks);
    }
}
