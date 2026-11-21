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
    //   AwaitingBudget -> Procuring -> AwaitingArrival -> Arrived -> Delivered
    // AwaitingDeptHead: no longer a stage. Tickets used to wait here for
    //                   the department head; they now go straight to the
    //                   Principal. Still written as the first step of
    //                   every ticket, where it means "submitted".
    // AwaitingBudget:   submitted; the Principal (or an Administrator)
    //                   reads the reason and approves or rejects.
    // Procuring:        Principal approved; a Property Officer is buying it.
    // AwaitingArrival:  bought or obtained; waiting for it to be delivered,
    //                   which a Property Officer confirms.
    // Arrived:          it is at the school and still has to be brought to
    //                   the requester's location (the Delivery stage). An
    //                   Property Officer marks it Delivered, or records that a
    //                   delivery could not be made and tries again.
    // Delivered:        brought to the location; the request is finished.
    // DeliveryFailed:   never a ticket's status. Only written as a step,
    //                   for a delivery that could not be made.
    // Rejected by whoever holds it at any open stage, with a reason.
    // Cancelled is kept for older rows. Names fit the 20-character
    // status column.
    public enum NewItemStatus
    {
        AwaitingDeptHead,
        AwaitingBudget,
        Procuring,
        AwaitingArrival,
        Arrived,
        Rejected,
        Cancelled,
        Delivered,
        DeliveryFailed,
        // A step only, never a request's status: Property found part of a
        // requisition short in stock and sent it back to Purchasing to buy
        // (Scripts/031).
        SentToPurchasing
    }
}
