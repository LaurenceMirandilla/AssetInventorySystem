namespace SchoolInventoryManagement.Web.Models
{
    public class ErrorViewModel
    {
        // The request's TraceIdentifier (e.g. 0HN7Q2KJ8M1:00000004). Shown
        // on a 500 for the user to pass on, and written into that error's
        // entry on Reports > Audit (ExceptionHandlingMiddleware), so the
        // Administrator can find it.
        public string? RequestId { get; set; }

        // Set from the status code the pipeline was re-executing when it
        // reached HomeController.Error. 500 is the fallback for the case
        // where somebody browses to /Home/Error directly.
        public int StatusCode { get; set; } = 500;

        // A fault on our side (500 and the other 5xx) rather than a link
        // or a request that can't be served.
        public bool IsServerError => StatusCode >= 500;

        public bool ShowRequestId => IsServerError && !string.IsNullOrEmpty(RequestId);

        public string Heading => StatusCode switch
        {
            404 => "We can't find that page",
            403 => "You don't have access to this page",
            400 => "That request wasn't valid",
            _ when IsServerError => "Something went wrong on our side",
            _ => "That didn't work"
        };

        // Deliberately vague about the 500 case: the exception itself is
        // logged, not shown, so an error page can never leak a connection
        // string or a stack trace to whoever happened to trip it.
        public string Detail => StatusCode switch
        {
            404 => "The link may be old, or the item was deleted. Check the address, or start again from one of these.",
            403 => "Your role doesn't cover this page. If you think it should, ask the Administrator to check your role.",
            400 => "Something about that link or form was malformed. Try again from the page you started on.",
            _ when IsServerError => "Nothing you entered was saved. Try again, and if it keeps happening, tell the Administrator this code:",
            _ => "The request couldn't be completed. Try again from the page you started on."
        };
    }
}
