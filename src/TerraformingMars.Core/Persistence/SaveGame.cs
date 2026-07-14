namespace TerraformingMars.Core.Persistence;

/// <summary>Serializable στιγμιότυπο παιχνιδιού (JSON). Ο χάρτης ανακατασκευάζεται από το seed + overrides.</summary>
public sealed class SaveGame
{
    public int Version { get; set; } = 3;

    /// <summary>Εμφανιζόμενο όνομα του save (π.χ. "Save" ή "Auto 1"). Κενό για παλιά αρχεία.</summary>
    public string Name { get; set; } = "";

    /// <summary>Χρονική στιγμή αποθήκευσης σε ISO-8601 UTC ("o"). Κενό για παλιά αρχεία.</summary>
    public string SavedAtUtc { get; set; } = "";

    public int Seed { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long Ticks { get; set; }
    public string Speed { get; set; } = "Normal";
    public string SponsorId { get; set; } = "normal";
    public int Crew { get; set; }

    /// <summary>Ενεργή Φάση 2 (latched μετά την ολοκλήρωση terraforming). Παλιά saves → false.</summary>
    public bool Phase2Active { get; set; }
    /// <summary>Αφηρημένος μεταναστευτικός πληθυσμός Φάσης 2. Παλιά saves → 0.</summary>
    public double Population { get; set; }

    /// <summary>Μέγιστος πληθυσμός που επιτεύχθηκε ποτέ (για μόνιμο ξεκλείδωμα κατωφλιών). Παλιά saves → 0.</summary>
    public double PeakPopulation { get; set; }

    /// <summary>Έφτασε το κατώφλι Urbanization (10k) — latched. Παλιά saves → false.</summary>
    public bool UrbanizationReached { get; set; }

    /// <summary>Έφτασε το κατώφλι Industrial Shift (50k) — latched. Παλιά saves → false.</summary>
    public bool IndustrialShiftReached { get; set; }

    /// <summary>Συσσωρευμένη σεισμική αστάθεια (Φάση 2B). Παλιά saves → 0.</summary>
    public double SeismicStress { get; set; }

    /// <summary>Συσσωρευμένη ενέργεια καταιγίδας (Φάση 2B). Παλιά saves → 0.</summary>
    public double StormStress { get; set; }

    /// <summary>Έγκριση παρατάξεων Φάσης 2 (0..1). Παλιά saves → 0.6 (ουδέτερο, όχι απεργία).</summary>
    public double IndustrialistApproval { get; set; } = 0.6;
    public double EcologistApproval { get; set; } = 0.6;

    public bool HasCaveShelter { get; set; }
    public double SolarEfficiency { get; set; } = 1.0;

    public PlanetSave Planet { get; set; } = new();
    public Dictionary<string, double> Resources { get; set; } = new();
    public Dictionary<string, double> Capacities { get; set; } = new();
    public TechSave Tech { get; set; } = new();
    public List<BuildingSave> Buildings { get; set; } = new();
    public List<ColonistSave> Colonists { get; set; } = new();
    public List<TileSave> TileOverrides { get; set; } = new();
    public List<EventSave> Events { get; set; } = new();
}

public sealed class PlanetSave
{
    public double Temperature { get; set; }
    public double Pressure { get; set; }
    public double Oxygen { get; set; }
    public double Water { get; set; }
    public double Biomass { get; set; }
}

public sealed class TechSave
{
    public List<string> Researched { get; set; } = new();
    public string? Current { get; set; }
    public double Progress { get; set; }

    /// <summary>Ξεκλειδωμένο tech tier Φάσης 2. Παλιά saves → false.</summary>
    public bool Phase2Unlocked { get; set; }
}

public sealed class BuildingSave
{
    public string Id { get; set; } = "";
    public int Q { get; set; }
    public int R { get; set; }
    public string State { get; set; } = "Operational";
    public int BuildProgress { get; set; }
    public double MaterialsPaid { get; set; }
    public bool Stalled { get; set; }
    public bool DepositDepleted { get; set; }
    public int RepairTicksRemaining { get; set; }
    public long CreatedTick { get; set; }
}

public sealed class ColonistSave
{
    public string Name { get; set; } = "";
    public string Specialty { get; set; } = "None";
    public double Health { get; set; } = 1.0;
    public double Morale { get; set; } = 1.0;
    public int AssignmentIndex { get; set; } = -1; // index στη λίστα Buildings, -1 = idle
}

public sealed class TileSave
{
    public int Q { get; set; }
    public int R { get; set; }
    public string Terrain { get; set; } = "Flatland";
    public double Remaining { get; set; }

    /// <summary>Τοπική ρύπανση (Φάση 2). Παλιά saves → 0.</summary>
    public double Pollution { get; set; }
}

public sealed class EventSave
{
    public string Type { get; set; } = "DustStorm";
    public int TicksRemaining { get; set; }
}
