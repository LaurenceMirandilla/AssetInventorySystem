namespace SchoolInventoryManagement.DAL.Entities.Enums
{
    // Lifecycle of a request:
    //   Pending -> InTransit -> Assigned -> Returned
    //   (or Pending -> Rejected / Cancelled)
    // InTransit: approved, unit reserved -- waiting at the pickup point
    //            (Borrow) or on its way to the destination (Transfer).
    // Assigned:  collected by / delivered to the requester.
    // Returned:  staff recorded the unit coming back.
    // Approved and Fulfilled are what requests used before this lifecycle;
    // they stay so those older rows still read correctly. Approved is also
    // the brief state inside the approval transaction.
    public enum RequestStatus
    {
        Pending,
        Approved,
        Rejected,
        Fulfilled,
        Cancelled,
        InTransit,
        Assigned,
        Returned
    }

    // Stages of a new-item request (NewItemRequest), separate from
    // RequestStatus because the two flows have nothing in common past
    // "Rejected":
    //   AwaitingDeptHead -> AwaitingBudget -> Procuring -> Arrived
    // AwaitingDeptHead: submitted, waiting for the requester's department
    //                   head (or an Administrator) to approve.
    // AwaitingBudget:   department head approved; an Asset Officer or
    //                   Administrator checks there is budget for it.
    // Procuring:        budget approved; being bought.
    // Arrived:          delivered; the requester has been told.
    // Rejected at either of the first two stages. Cancelled is kept for
    // older rows. Names fit the 20-character status column.
    public enum NewItemStatus
    {
        AwaitingDeptHead,
        AwaitingBudget,
        Procuring,
        Arrived,
        Rejected,
        Cancelled
    }
}
