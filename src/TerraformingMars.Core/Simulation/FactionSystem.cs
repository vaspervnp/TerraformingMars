using TerraformingMars.Core.Buildings;

namespace TerraformingMars.Core.Simulation;

/// <summary>
/// Πολιτικές παρατάξεις της Φάσης 2. Το πλήρωμα/κοινωνία χωρίζεται σε δύο μπλοκ με αντίθετες
/// επιθυμίες: <b>Βιομηχανικοί</b> (θέλουν ενεργή βιομηχανία) και <b>Οικολόγοι</b> (θέλουν
/// βλάστηση/σταθερό κλίμα). Η έγκριση κάθε παράταξης κινείται ομαλά προς έναν στόχο που εξαρτάται
/// από την <i>ισορροπία</i> βιομηχανίας-vs-οικολογίας· χαμηλή έγκριση → <b>απεργία</b> που σταματά
/// τα κτίρια της κατηγορίας. Τα District Town Halls (διακυβέρνηση) ανεβάζουν και τις δύο εγκρίσεις.
/// </summary>
public sealed class FactionSystem : ISimulationSystem
{
    private const double DriftRate = 0.01;          // πόσο γρήγορα η έγκριση πλησιάζει τον στόχο
    private const double StrikeThreshold = 0.25;    // κάτω από αυτό → απεργία
    private const double StrikeEndThreshold = 0.35; // πάνω από αυτό → λήξη απεργίας (hysteresis)
    private const double BalanceWeight = 0.4;
    private const double GovernancePerHall = 0.15;
    private const double MaxGovernance = 0.30;
    private const double RunawayEcologistPenalty = 0.30;
    private const int IndustryForFullScore = 5;     // πλήθος ενεργών Industry κτιρίων για score 1.0

    public void Tick(World world)
    {
        if (!world.Phase2Active) { world.IndustrialStrike = false; world.EcologistStrike = false; return; }

        var colony = world.Colony;

        int industryCount = 0, townHalls = 0;
        foreach (var b in colony.Buildings)
        {
            if (b.State != BuildingState.Operational) continue;
            if (b.Definition.Category == "Industry") industryCount++;
            if (b.Definition.Id == "district_town_hall") townHalls++;
        }

        double industryScore = Math.Min(1.0, industryCount / (double)IndustryForFullScore);
        double ecologyScore = world.Planet.Biomass;                 // 0..1
        double balance = industryScore - ecologyScore;              // -1..1
        double gov = Math.Min(MaxGovernance, GovernancePerHall * townHalls);

        double indTarget = Clamp01(0.5 + BalanceWeight * balance + gov);
        double ecoTarget = Clamp01(0.5 - BalanceWeight * balance + gov - (world.RunawayActive ? RunawayEcologistPenalty : 0));

        colony.IndustrialistApproval = Drift(colony.IndustrialistApproval, indTarget);
        colony.EcologistApproval = Drift(colony.EcologistApproval, ecoTarget);

        world.IndustrialStrike = OnStrike(world.IndustrialStrike, colony.IndustrialistApproval);
        world.EcologistStrike = OnStrike(world.EcologistStrike, colony.EcologistApproval);
    }

    private static double Drift(double value, double target) => value + (target - value) * DriftRate;

    // Hysteresis: μπαίνει σε απεργία κάτω από το κατώφλι, βγαίνει μόνο όταν ανακάμψει αρκετά.
    private static bool OnStrike(bool striking, double approval) =>
        striking ? approval < StrikeEndThreshold : approval < StrikeThreshold;

    private static double Clamp01(double v) => Math.Clamp(v, 0.0, 1.0);
}
