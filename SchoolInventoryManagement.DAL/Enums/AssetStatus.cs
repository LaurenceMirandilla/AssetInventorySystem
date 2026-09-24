namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    public enum AssetStatus
    {
        Available,
        Assigned,
        InTransit,
        UnderMaintenance,
        Reserved,
        Lost,
        Damaged,
        Disposed,

        // Picked up on a request and not back by its ReturnBy. Set only by
        // the overdue check (OverdueService); a return clears it like Assigned.
        Overdue
    }
}