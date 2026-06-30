namespace TerraformingMars.Core.Events;

/// <summary>Τύποι τυχαίων γεγονότων που διακόπτουν τη μονοτονία.</summary>
public enum EventType
{
    DustStorm,           // -80% ηλιακή ενέργεια
    SolarFlare,          // ακτινοβολία: αρρωσταίνει αποίκους & καίει ηλεκτρονικά (χωρίς μαγνητόσφαιρα/σπήλαιο)
    LifeSupportFailure,  // βλάβη συστήματος O2/νερού — χρειάζεται Μηχανικό για επισκευή
    CaveDiscovery        // (θετικό) φυσική θωράκιση από ακτινοβολία
}
