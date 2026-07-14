namespace TerraformingMars.Core.Colonists;

/// <summary>
/// Ειδικότητα αποίκου. Η σωστή ανάθεση σε κτίριο που ταιριάζει δίνει bonus απόδοσης.
/// </summary>
public enum Specialty
{
    None,
    Geologist,     // yield ορυχείων/γεωτρύπανων, εντοπισμός κρυφών κοιτασμάτων
    Engineer,      // ταχύτητα κατασκευής, απόδοση εργοστασίων, λιγότερες βλάβες
    Botanist,      // παραγωγή τροφής, ανάπτυξη χλωρίδας (late game)
    Climatologist, // bonus σε δράσεις πλανητικής μηχανικής, πρόβλεψη καιρού
    Doctor         // περιορίζει την εξέλιξη της Άρειας Πανώλης (Φάση 2), στελεχώνει Isolation Hospitals
}
