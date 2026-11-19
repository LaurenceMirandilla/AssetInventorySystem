namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    // What a physical count found for one unit (AssetCountLine).
    public enum AssetCountResult
    {
        // On the room's list and there.
        Found,

        // On the room's list and not there.
        Missing,

        // There, but the system had it somewhere else.
        FoundElsewhere
    }
}
