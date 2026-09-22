namespace SchoolInventoryManagement.Web.Models
{
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);

        // Set from the status code the pipeline was re-executing when it
        // reached HomeController.Error. 500 is the fallback for the case
        // where somebody browses to /Home/Error directly.
        public int StatusCode { get; set; } = 500;

        public string Heading => StatusCode switch
        {
            404 => "We couldn't find that page",
            403 => "You don't have access to that",
            400 => "That request wasn't valid",
            _   => "Something went wrong"
        };

        // Deliberately vague about the 500 case: the exception itself is
        // logged, not shown, so an error page can never leak a connection
        // string or a stack trace to whoever happened to trip it.
        public string Detail => StatusCode switch
        {
            404 => "The page or record you asked for doesn't exist, or it may have been removed since the link was made.",
            403 => "Your role doesn't cover this page. If you think it should, ask an administrator.",
            400 => "Something about that link or form was malformed. Try again from the page you started on.",
            _   => "The request couldn't be completed. Nothing was saved, so it's safe to try again."
        };
    }
}
