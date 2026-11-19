namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    public enum AssetStatus
    {
        Available,
        Assigned,
        InTransit,
        UnderMaintenance,
        Disposed,

        // Picked up on a request and not back by its ReturnBy. Set only by
        // the overdue check (OverdueService); a return clears it like Assigned.
        Overdue,

        // Red tagged by the Property Department for disposal (Scripts/025):
        // out of use, not borrowable, waiting to be disposed of.
        RedTagged
    }
}