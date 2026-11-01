using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Helpers
{
    // A new-item request's place in its five stages, for the request lists
    // (All New-Item Requests, New-Item Approvals): the progress strip, the
    // short stage label, and who it waits on.
    public static class NewItemProgress
    {
        public static readonly NewItemStatus[] Open =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival
        };

        // Stage label, and the step status that completes it.
        private static readonly (string Label, NewItemStatus DoneBy)[] Stages =
        {
            ("Department head", NewItemStatus.AwaitingBudget),
            ("Principal", NewItemStatus.Procuring),
            ("Procurement", NewItemStatus.AwaitingArrival),
            ("Arrival", NewItemStatus.Arrived),
            ("Arrived", NewItemStatus.Arrived)
        };

        public static bool IsStopped(NewItemRequestResponseDTO r) =>
            r.RequestStatus is NewItemStatus.Rejected or NewItemStatus.Cancelled;

        // How many of the five stages are behind it.
        public static int StagesDone(NewItemRequestResponseDTO r) => r.RequestStatus switch
        {
            NewItemStatus.AwaitingDeptHead => 0,
            NewItemStatus.AwaitingBudget => 1,
            NewItemStatus.Procuring => 2,
            NewItemStatus.AwaitingArrival => 3,
            NewItemStatus.Arrived => 5,
            // Rejected or cancelled: as far as its steps got before that.
            _ => r.Steps.Count(s => s.Status is NewItemStatus.AwaitingBudget or NewItemStatus.Procuring
                                     or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived)
        };

        // The short label on list pills.
        public static string Short(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "Department head",
            NewItemStatus.AwaitingBudget => "Principal",
            NewItemStatus.Procuring => "To procure",
            NewItemStatus.AwaitingArrival => "Waiting arrival",
            NewItemStatus.Arrived => "Arrived",
            NewItemStatus.Rejected => "Rejected",
            NewItemStatus.Cancelled => "Cancelled",
            _ => s.ToString()
        };

        // Pill colour class (asset-pill st-*).
        public static string Pill(NewItemStatus s) => s switch
        {
            NewItemStatus.Arrived => "st-available",
            NewItemStatus.Rejected => "st-overdue",
            NewItemStatus.Cancelled => "st-disposed",
            NewItemStatus.Procuring or NewItemStatus.AwaitingArrival => "st-intransit",
            _ => "st-undermaintenance"
        };

        // Who has to act next, for the read-only overview.
        public static string WaitingOn(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "the department head",
            NewItemStatus.AwaitingBudget => "the Principal (budget and reason check)",
            NewItemStatus.Procuring => "an Asset Officer to procure it",
            NewItemStatus.AwaitingArrival => "an Asset Officer to confirm it arrived",
            _ => ""
        };

        // What a step did, said after the person's name.
        public static string StepText(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "submitted the request",
            NewItemStatus.AwaitingBudget => "approved for the department",
            NewItemStatus.Procuring => "approved the budget",
            NewItemStatus.AwaitingArrival => "marked it procured",
            NewItemStatus.Arrived => "confirmed it arrived",
            NewItemStatus.Rejected => "rejected the request",
            NewItemStatus.Cancelled => "cancelled the request",
            _ => s.ToString()
        };

        // What happens after the current stage's button.
        public static string NextNote(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "Next, the Principal checks budget and reason.",
            NewItemStatus.AwaitingBudget => "Next, an Asset Officer procures it.",
            NewItemStatus.Procuring => "Next, it waits for arrival.",
            NewItemStatus.AwaitingArrival => "That completes the request.",
            _ => ""
        };

        // The progress strip. currentNote labels the stage it is at
        // ("waiting", or "you" when the viewer acts on it).
        public static List<RequestStep> Steps(NewItemRequestResponseDTO r, string currentNote)
        {
            var done = StagesDone(r);
            var stopped = IsStopped(r);
            var last = r.Steps.LastOrDefault();
            var steps = new List<RequestStep>();
            for (var i = 0; i < Stages.Length; i++)
            {
                var doneStep = r.Steps.LastOrDefault(s => s.Status == Stages[i].DoneBy);
                if (i < done)
                    steps.Add(new RequestStep(Stages[i].Label, doneStep?.ActedAt.ToString("MMM d"), RequestStepState.Done));
                else if (i == done && stopped)
                    steps.Add(new RequestStep(r.RequestStatus == NewItemStatus.Rejected ? "Rejected" : "Cancelled",
                        last?.ActedAt.ToString("MMM d"), RequestStepState.Stopped));
                else if (i == done)
                    steps.Add(new RequestStep(Stages[i].Label, currentNote, RequestStepState.Current));
                else
                    steps.Add(new RequestStep(Stages[i].Label, null, RequestStepState.Todo));
            }
            return steps;
        }

        public static string Initials(string name)
        {
            var initials = string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => p[0])).ToUpperInvariant();
            return initials.Length > 2 ? initials[..2] : initials;
        }
    }
}
