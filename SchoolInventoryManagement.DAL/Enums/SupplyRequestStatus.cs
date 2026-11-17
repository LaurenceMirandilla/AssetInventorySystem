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
        // Taken off the shelf and on its way to the requester's location.
        // Stock went down at this point.
        Released,
        Rejected,
        Cancelled,
        // Brought to the location and received. The request is finished.
        Delivered
    }
}
