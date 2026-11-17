namespace SchoolInventoryManagement.BLL.DTOs
{
    // What an open page checks every few seconds (LiveController): the
    // sidebar counts, and whether what the page shows has changed.
    public class LiveStatusDTO
    {
        // Unread notifications.
        public int Unread { get; set; }

        // Requests waiting for this person's decision (the number after
        // "Approvals"). Null for roles that do not approve.
        public int? Approvals { get; set; }

        // A short text that changes whenever the data the page shows
        // changes. Null when the page did not say what it shows.
        public string? Stamp { get; set; }

        // The newest unread notification, for the pop-up.
        public LiveNotificationDTO? Latest { get; set; }
    }

    public class LiveNotificationDTO
    {
        public int Id { get; set; }
        public string Message { get; set; } = "";
    }
}
