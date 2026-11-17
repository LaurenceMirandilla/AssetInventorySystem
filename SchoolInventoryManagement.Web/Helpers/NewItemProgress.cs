using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Helpers
{
    // A new-item request's place in its four stages (Principal,
    // Procurement, Arrival, Delivery), for the request lists
    // (All New-Item Requests, New-Item Approvals): the progress strip, the
    // short stage label, and who it waits on.
    public static class NewItemProgress
    {
        // Stages at which the ticket can still be rejected.
        public static readonly NewItemStatus[] Open =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival
        };

        // Not finished yet: the stages above, and Delivery (arrived at the
        // school, still to be brought to the requester's location).
        public static readonly NewItemStatus[] Active =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival, NewItemStatus.Arrived
        };

        // At the Delivery stage, the last try could not be made: that
        // step (its remarks say why). Null otherwise.
        public static NewItemRequestStepDTO? DeliveryProblem(NewItemRequestResponseDTO r)
        {
            if (r.RequestStatus != NewItemStatus.Arrived)
                return null;
            var last = r.Steps.LastOrDefault();
            return last is not null && last.Status == NewItemStatus.DeliveryFailed ? last : null;
        }

        // Stage label, and the step status that completes it.
        public static readonly (string Label, NewItemStatus DoneBy)[] Stages =
        {
            ("Principal", NewItemStatus.Procuring),
            ("Procurement", NewItemStatus.AwaitingArrival),
            ("Arrival", NewItemStatus.Arrived),
            ("Delivery", NewItemStatus.Delivered)
        };

        public static bool IsStopped(NewItemRequestResponseDTO r) =>
            r.RequestStatus is NewItemStatus.Rejected or NewItemStatus.Cancelled;

        public const int StageCount = 4;

        // How many of the four stages are behind it.
        public static int StagesDone(NewItemRequestResponseDTO r) => r.RequestStatus switch
        {
            NewItemStatus.AwaitingDeptHead => 0,
            NewItemStatus.AwaitingBudget => 0,
            NewItemStatus.Procuring => 1,
            NewItemStatus.AwaitingArrival => 2,
            NewItemStatus.Arrived => 3,
            NewItemStatus.Delivered => StageCount,
            // Rejected or cancelled: as far as its steps got before that.
            _ => r.Steps.Select(s => s.Status).Distinct().Count(s =>
                    s is NewItemStatus.Procuring or NewItemStatus.AwaitingArrival
                      or NewItemStatus.Arrived or NewItemStatus.Delivered)
        };

        // "Assets" or "Supplies".
        public static string Kind(NewItemRequestResponseDTO r) =>
            r.ItemKind == CategoryKind.Supply ? "Supplies" : "Assets";

        // "3 items · 15 units", or "1 item" when that says it all.
        public static string Units(NewItemRequestResponseDTO r)
        {
            var units = r.Items.Sum(i => i.Quantity);
            return $"{r.Items.Count} item{(r.Items.Count == 1 ? "" : "s")} · {units} unit{(units == 1 ? "" : "s")}";
        }

        // The short label on list pills.
        public static string Short(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "Principal",
            NewItemStatus.AwaitingBudget => "Principal",
            NewItemStatus.Procuring => "To procure",
            NewItemStatus.AwaitingArrival => "Waiting arrival",
            NewItemStatus.Arrived => "To deliver",
            NewItemStatus.Delivered => "Delivered",
            NewItemStatus.Rejected => "Rejected",
            NewItemStatus.Cancelled => "Cancelled",
            _ => s.ToString()
        };

        // Pill colour class (asset-pill st-*).
        public static string Pill(NewItemStatus s) => s switch
        {
            NewItemStatus.Delivered => "st-available",
            NewItemStatus.Rejected => "st-overdue",
            NewItemStatus.Cancelled => "st-disposed",
            NewItemStatus.Procuring or NewItemStatus.AwaitingArrival or NewItemStatus.Arrived => "st-intransit",
            _ => "st-undermaintenance"
        };

        // Who has to act next, for the read-only overview.
        public static string WaitingOn(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "the Principal",
            NewItemStatus.AwaitingBudget => "the Principal",
            NewItemStatus.Procuring => "an Asset Officer to procure it",
            NewItemStatus.AwaitingArrival => "an Asset Officer to confirm it arrived",
            NewItemStatus.Arrived => "an Asset Officer to deliver it",
            _ => ""
        };

        // What a step did, said after the person's name.
        public static string StepText(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "submitted the request",
            NewItemStatus.AwaitingBudget => "approved for the department",
            NewItemStatus.Procuring => "approved the request",
            NewItemStatus.AwaitingArrival => "marked it procured",
            NewItemStatus.Arrived => "confirmed it arrived",
            NewItemStatus.Delivered => "delivered it",
            NewItemStatus.DeliveryFailed => "could not deliver it",
            NewItemStatus.Rejected => "rejected the request",
            NewItemStatus.Cancelled => "cancelled the request",
            _ => s.ToString()
        };

        // What happens after the current stage's button.
        public static string NextNote(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "Next, an Asset Officer procures it.",
            NewItemStatus.AwaitingBudget => "Next, an Asset Officer procures it.",
            NewItemStatus.Procuring => "Next, it waits for arrival.",
            NewItemStatus.AwaitingArrival => "Next, it is delivered.",
            NewItemStatus.Arrived => "Delivered completes the request.",
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
                else if (i == done && DeliveryProblem(r) is not null)
                    steps.Add(new RequestStep(Stages[i].Label, "not delivered yet", RequestStepState.Late));
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
