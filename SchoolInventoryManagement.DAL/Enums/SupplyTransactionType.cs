namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    // What changed a supply's stock. One row in SupplyTransactions each time.
    public enum SupplyTransactionType
    {
        // The quantity typed in when the supply was first added.
        Opening,
        // Stock that arrived (a new-item request, a purchase).
        Received,
        // Handed out on a supply request.
        Issued,
        // A count that did not match the system.
        Adjusted,
        // Moved to another location: out of this one...
        MovedOut,
        // ...and into that one.
        MovedIn,
        // Taken out of stock because it cannot be used: damaged, expired,
        // lost. Always with a reason.
        Removed
    }
}
