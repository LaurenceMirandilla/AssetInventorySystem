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
}