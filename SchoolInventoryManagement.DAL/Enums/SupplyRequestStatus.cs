namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    // Supplies are used up, so a supply request has no In transit,
    // Assigned, Returned or Overdue. It ends at Released.
    public enum SupplyRequestStatus
    {
        // Waiting for an approver.
        Pending,
        // Amounts confirmed and a pick-up room set; ready to collect.
        Approved,
        // Handed over. Stock went down at this point.
        Released,
        Rejected,
        Cancelled
    }
}
