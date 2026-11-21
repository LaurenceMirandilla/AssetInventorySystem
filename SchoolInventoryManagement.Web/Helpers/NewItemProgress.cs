using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.ViewModels;

namespace SchoolInventoryManagement.Web.Helpers
{
    // A requisition's place in its stages (Scripts/031), for the lists, the
    // queues and its page: the progress strip, the short label, and who it
    // waits on.
    //
    //   something to buy:  Procurement -> Property -> Receive
    //   all in stock:      Property -> Receive
    //
    // Procurement: Purchasing orders what is missing; the supplier delivers.
    // Property: the Property Department receives it, picks the rest from
    // stock and sends everything. Receive: the requester confirms.
    public static class NewItemProgress
    {
        // Stages at which a requisition can still be rejected: until it is
        // sent out.
        public static readonly NewItemStatus[] Open =
        {
            NewItemStatus.AwaitingDeptHead, NewItemStatus.AwaitingBudget,
            NewItemStatus.Procuring, NewItemStatus.AwaitingArrival, NewItemStatus.Arrived
        };

        // Not sent out yet.
        public static readonly NewItemStatus[] Active = Open;

        // At Property, the last try to send it could not be made: that step
        // (its remarks say why). Null otherwise.
        public static NewItemRequestStepDTO? DeliveryProblem(NewItemRequestResponseDTO r)
        {
            if (r.RequestStatus != NewItemStatus.Arrived)
                return null;
            var last = r.Steps.LastOrDefault();
            return last is not null && last.Status == NewItemStatus.DeliveryFailed ? last : null;
        }

        public static bool IsStopped(NewItemRequestResponseDTO r) =>
            r.RequestStatus is NewItemStatus.Rejected or NewItemStatus.Cancelled;

        // The requester confirms receiving it: consumables always; fixed
        // assets unless delivered before units were registered against them
        // (nothing to confirm). Needs RegisteredUnits filled (the service
        // does).
        public static bool HasReceipt(NewItemRequestResponseDTO r) =>
            !(r.ItemKind == CategoryKind.Asset && r.RequestStatus == NewItemStatus.Delivered
              && r.RegisteredUnits.Count == 0 && r.AcknowledgedAt is null);

        // Its stages, in order.
        public static List<string> StageLabels(NewItemRequestResponseDTO r)
        {
            var labels = new List<string>();
            if (r.NeedsPurchase)
                labels.Add("Procurement");
            labels.Add("Property");
            if (HasReceipt(r))
                labels.Add("Receive");
            return labels;
        }

        public static int StageCountFor(NewItemRequestResponseDTO r) => StageLabels(r).Count;

        // Still going: not sent yet, or sent and waiting for the requester.
        public static bool IsActive(NewItemRequestResponseDTO r) =>
            Active.Contains(r.RequestStatus) || r.AwaitsAcknowledgment;

        // Who it waits on, the receipt included.
        public static string WaitingOn(NewItemRequestResponseDTO r) =>
            r.AwaitsAcknowledgment ? $"{r.RequestedByUserName} to confirm receiving it" : WaitingOn(r.RequestStatus);

        // The list pill.
        public static string Short(NewItemRequestResponseDTO r) =>
            r.AwaitsAcknowledgment ? "To confirm"
            : r.RequestStatus == NewItemStatus.Delivered && r.AcknowledgedAt is not null ? "Received"
            : Short(r.RequestStatus);

        // How many of its stages are behind it.
        public static int StagesDone(NewItemRequestResponseDTO r)
        {
            var procurement = r.NeedsPurchase ? 1 : 0;
            return r.RequestStatus switch
            {
                NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget
                    or NewItemStatus.Procuring or NewItemStatus.AwaitingArrival => 0,
                NewItemStatus.Arrived => procurement,
                NewItemStatus.Delivered => procurement + 1 + (HasReceipt(r) && r.AcknowledgedAt is not null ? 1 : 0),
                // Rejected or cancelled: as far as its steps got before that.
                _ => r.Steps.Any(s => s.Status == NewItemStatus.Delivered) ? procurement + 1
                    : r.NeedsPurchase && r.Steps.Any(s => s.Status == NewItemStatus.Arrived) ? 1
                    : 0
            };
        }

        // "Fixed assets" or "Consumables".
        public static string Kind(NewItemRequestResponseDTO r) =>
            r.ItemKind == CategoryKind.Supply ? "Consumables" : "Fixed assets";

        // "3 items · 15 units", or "1 item" when that says it all.
        public static string Units(NewItemRequestResponseDTO r)
        {
            var units = r.Items.Sum(i => i.Quantity);
            return $"{r.Items.Count} item{(r.Items.Count == 1 ? "" : "s")} · {units} unit{(units == 1 ? "" : "s")}";
        }

        // The short label on list pills.
        public static string Short(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget or NewItemStatus.Procuring => "To buy",
            NewItemStatus.AwaitingArrival => "Ordered",
            NewItemStatus.Arrived => "At Property",
            NewItemStatus.Delivered => "Sent",
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
            NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget or NewItemStatus.Procuring
                => "Purchasing to order what is missing",
            NewItemStatus.AwaitingArrival => "the supplier; Property receives it",
            NewItemStatus.Arrived => "Property to get it ready and send it",
            _ => ""
        };

        // What a step did, said after the person's name.
        public static string StepText(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead => "made the requisition",
            NewItemStatus.AwaitingBudget => "approved it for the department",
            // Before requisitions: the Principal's approval.
            NewItemStatus.Procuring => "approved the request",
            NewItemStatus.AwaitingArrival => "ordered what was missing",
            NewItemStatus.Arrived => "received it from the supplier",
            NewItemStatus.Delivered => "sent it out",
            NewItemStatus.DeliveryFailed => "could not send it",
            NewItemStatus.SentToPurchasing => "sent what was short in stock to Purchasing",
            NewItemStatus.Rejected => "rejected the requisition",
            NewItemStatus.Cancelled => "cancelled the requisition",
            _ => s.ToString()
        };

        // What happens after the current stage's button.
        public static string NextNote(NewItemStatus s) => s switch
        {
            NewItemStatus.AwaitingDeptHead or NewItemStatus.AwaitingBudget => "Next, Purchasing orders what is missing.",
            NewItemStatus.Procuring => "Next, it waits for the supplier.",
            NewItemStatus.AwaitingArrival => "Next, the Property Department gets it ready.",
            NewItemStatus.Arrived => "Next, the requester confirms receiving it.",
            _ => ""
        };

        // The progress strip. currentNote labels the stage it is at
        // ("waiting", or "you" when the viewer acts on it).
        public static List<RequestStep> Steps(NewItemRequestResponseDTO r, string currentNote)
        {
            var labels = StageLabels(r);
            var done = StagesDone(r);
            var stopped = IsStopped(r);
            var last = r.Steps.LastOrDefault();

            string? DoneOn(string label) => label switch
            {
                "Procurement" => r.Steps.LastOrDefault(s => s.Status == NewItemStatus.Arrived)?.ActedAt.ToString("MMM d"),
                "Property" => r.Steps.LastOrDefault(s => s.Status == NewItemStatus.Delivered)?.ActedAt.ToString("MMM d"),
                _ => r.AcknowledgedAt?.ToString("MMM d")
            };

            var steps = new List<RequestStep>();
            for (var i = 0; i < labels.Count; i++)
            {
                if (i < done)
                    steps.Add(new RequestStep(labels[i], DoneOn(labels[i]), RequestStepState.Done));
                else if (i == done && stopped)
                    steps.Add(new RequestStep(r.RequestStatus == NewItemStatus.Rejected ? "Rejected" : "Cancelled",
                        last?.ActedAt.ToString("MMM d"), RequestStepState.Stopped));
                else if (i == done && DeliveryProblem(r) is not null)
                    steps.Add(new RequestStep(labels[i], "not sent yet", RequestStepState.Late));
                else if (i == done)
                    steps.Add(new RequestStep(labels[i],
                        labels[i] == "Receive" ? "requester confirms"
                        : labels[i] == "Procurement" && r.RequestStatus == NewItemStatus.AwaitingArrival ? "ordered"
                        : currentNote,
                        RequestStepState.Current));
                else
                    steps.Add(new RequestStep(labels[i], null, RequestStepState.Todo));
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
