namespace SchoolInventoryManagement.Web.ViewModels
{
    // One stage in a request's progress strip (Views/Shared/_RequestSteps).
    public class RequestStep
    {
        public RequestStep(string label, string? note, RequestStepState state)
        {
            Label = label;
            Note = note;
            State = state;
        }

        public string Label { get; }
        public string? Note { get; }      // a date, "waiting", "by Nov 7"...
        public RequestStepState State { get; }
    }

    public enum RequestStepState
    {
        Done,     // ticked
        Current,  // where it is now
        Late,     // past due (an overdue return)
        Stopped,  // rejected or cancelled here
        Todo      // not reached yet
    }
}
