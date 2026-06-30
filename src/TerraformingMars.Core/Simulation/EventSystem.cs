using TerraformingMars.Core.Buildings;
using TerraformingMars.Core.Colonists;
using TerraformingMars.Core.Events;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Τυχαία γεγονότα (αμμοθύελλες, εκλάμψεις, βλάβες, σπήλαια) με βάση το προφίλ χορηγού.
/// Ντετερμινιστικό (seeded RNG). Διαχειρίζεται επίσης την επισκευή κατεστραμμένων κτιρίων
/// (ταχύτερη με Μηχανικό) και την ανάρρωση/ασθένεια των αποίκων.
/// </summary>
public sealed class EventSystem : ISimulationSystem
{
    private const double FlareHealthDrainPerTick = 0.004;
    private const double HealthRegenPerTick = 0.0015;

    private readonly SponsorProfile _sponsor;
    private readonly Random _rng;

    public EventSystem(SponsorProfile sponsor, int seed)
    {
        _sponsor = sponsor;
        _rng = new Random(seed * 7919 + 13);
    }

    public void Tick(World world)
    {
        RepairDisabledBuildings(world);
        ProcessActiveEvents(world);

        world.SolarEfficiency = world.ActiveEvents.Any(e => e.Type == EventType.DustStorm)
            ? _sponsor.DustStormSolarFactor
            : 1.0;

        if (!world.ActiveEvents.Any(e => e.Type == EventType.SolarFlare))
            RegenHealth(world);

        if (_rng.NextDouble() < _sponsor.EventChancePerTick)
            Trigger(world, RandomEventType());
    }

    /// <summary>Πυροδοτεί ρητά ένα γεγονός (χρησιμοποιείται και από τα tests).</summary>
    public void Trigger(World world, EventType type)
    {
        switch (type)
        {
            case EventType.DustStorm:
                if (!world.ActiveEvents.Any(e => e.Type == EventType.DustStorm))
                {
                    world.ActiveEvents.Add(new ActiveEvent(EventType.DustStorm,
                        RandRange(_sponsor.DustStormMinTicks, _sponsor.DustStormMaxTicks)));
                    Notify(world, "Dust storm: solar power crippled");
                }
                break;

            case EventType.SolarFlare:
                world.ActiveEvents.Add(new ActiveEvent(EventType.SolarFlare,
                    RandRange(_sponsor.SolarFlareMinTicks, _sponsor.SolarFlareMaxTicks)));
                Notify(world, "Solar flare incoming");
                if (!IsProtected(world)) DisableRandom(world, "electronics fried by flare");
                break;

            case EventType.LifeSupportFailure:
                var target = PickOperational(world, "LifeSupport");
                if (target is not null)
                {
                    Disable(target, _sponsor.RepairTicks);
                    Notify(world, $"LIFE SUPPORT FAILURE: {target.Definition.Name} (send an Engineer!)");
                }
                break;

            case EventType.CaveDiscovery:
                if (!world.HasCaveShelter)
                {
                    world.HasCaveShelter = true;
                    Notify(world, "Cave discovered: natural radiation shelter");
                }
                break;
        }
    }

    private void RepairDisabledBuildings(World world)
    {
        foreach (var b in world.Colony.Buildings)
        {
            if (b.State != BuildingState.Disabled || b.RepairTicksRemaining <= 0) continue;

            int speed = b.Workers.Any(w => w.Specialty == Specialty.Engineer) ? 2 : 1;
            b.RepairTicksRemaining -= speed;
            if (b.RepairTicksRemaining <= 0)
            {
                b.RepairTicksRemaining = 0;
                b.State = BuildingState.Operational;
                Notify(world, $"{b.Definition.Name} repaired");
            }
        }
    }

    private void ProcessActiveEvents(World world)
    {
        for (int i = world.ActiveEvents.Count - 1; i >= 0; i--)
        {
            var e = world.ActiveEvents[i];

            if (e.Type == EventType.SolarFlare && !IsProtected(world))
                foreach (var c in world.Colony.Colonists)
                    c.Health = Math.Max(0, c.Health - FlareHealthDrainPerTick);

            e.TicksRemaining--;
            if (e.TicksRemaining <= 0)
            {
                world.ActiveEvents.RemoveAt(i);
                Notify(world, e.Type == EventType.DustStorm ? "Dust storm cleared" : "Solar flare passed");
            }
        }
    }

    private static void RegenHealth(World world)
    {
        foreach (var c in world.Colony.Colonists)
            if (c.Health < 1.0) c.Health = Math.Min(1.0, c.Health + HealthRegenPerTick);
    }

    private EventType RandomEventType()
    {
        double r = _rng.NextDouble();
        if (r < 0.40) return EventType.DustStorm;
        if (r < 0.70) return EventType.LifeSupportFailure;
        if (r < 0.92) return EventType.SolarFlare;
        return EventType.CaveDiscovery;
    }

    private Building? PickOperational(World world, string category)
    {
        var candidates = world.Colony.Buildings
            .Where(b => b.State == BuildingState.Operational && b.Definition.Category == category)
            .ToList();
        return candidates.Count == 0 ? null : candidates[_rng.Next(candidates.Count)];
    }

    private void DisableRandom(World world, string reason)
    {
        var candidates = world.Colony.Buildings
            .Where(b => b.State == BuildingState.Operational && b.Definition.Category != "Power")
            .ToList();
        if (candidates.Count == 0) return;

        var b = candidates[_rng.Next(candidates.Count)];
        Disable(b, _sponsor.RepairTicks);
        Notify(world, $"{b.Definition.Name}: {reason}");
    }

    private static void Disable(Building b, int repairTicks)
    {
        b.State = BuildingState.Disabled;
        b.RepairTicksRemaining = repairTicks;
    }

    private static bool IsProtected(World world) =>
        world.HasCaveShelter ||
        world.Colony.Buildings.Any(b => b.State == BuildingState.Operational && b.Definition.ShieldsAtmosphere);

    private int RandRange(int min, int max) => _rng.Next(min, max + 1);

    private static void Notify(World world, string message)
    {
        world.EventNotifications.Add(message);
        if (world.EventNotifications.Count > 6)
            world.EventNotifications.RemoveAt(0);
    }
}
