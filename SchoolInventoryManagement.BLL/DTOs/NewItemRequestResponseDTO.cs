using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class NewItemRequestResponseDTO
    {
        public int NewItemRequestID { get; set; }

        public int RequestedByUserID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string? RequestedByRoleName { get; set; }
        public string DepartmentName { get; set; } = null!;

        public int DepartmentID { get; set; }

        // A summary of the ticket ("Document camera, HDMI cable +3") and
        // its total units; the lines are in Items.
        public string ItemName { get; set; } = null!;
        public int Quantity { get; set; }

        // Assets or supplies.
        public CategoryKind ItemKind { get; set; }

        // Where the items are brought when they arrive. Null on tickets
        // made before it was asked for.
        public int? DeliverToLocationID { get; set; }
        public string? DeliverToLocationName { get; set; }
        public string? DeliverToBranchName { get; set; }

        // What the ticket asks for, one line per item (at least one on every
        // request once Scripts/014 has run).
        public List<NewItemRequestItemDTO> Items { get; set; } = new();

        // "Science Lab 1 · Main Campus", or null.
        public string? DeliverToText => DeliverToLocationName is null
            ? null
            : DeliverToBranchName is null ? DeliverToLocationName : $"{DeliverToLocationName} · {DeliverToBranchName}";
        public string? Reason { get; set; }

        // Null only on requests made before this was asked for.
        public DateTime? NeededBy { get; set; }

        public DateTime RequestDate { get; set; }
        public NewItemStatus RequestStatus { get; set; }

        public string? ReviewedByUserName { get; set; }
        public DateTime? ReviewDate { get; set; }
        public string? Remarks { get; set; }

        // Carried to the view so Approve/Reject can post the token back and
        // lose the race rather than silently overwriting another reviewer.
        public byte[] RowVersion { get; set; } = null!;

        // When the requester confirmed receiving the assets.
        public DateTime? AcknowledgedAt { get; set; }

        // The units registered for this request (assets only), filled by
        // the service where it is shown.
        public List<RegisteredUnitDTO> RegisteredUnits { get; set; } = new();

        // How many units the ticket's lines ask for.
        public int OrderedUnits => Items.Sum(i => i.Quantity);

        // Delivered assets the requester has not confirmed yet.
        public bool AwaitsAcknowledgment =>
            ItemKind == CategoryKind.Asset && RequestStatus == NewItemStatus.Delivered
            && AcknowledgedAt is null && RegisteredUnits.Count > 0;

        // Every stage so far, oldest first. Filled on the details page only.
        public List<NewItemRequestStepDTO> Steps { get; set; } = new();

        // Whether the signed-in user can move this request on (or reject
        // it) at its current stage. Set by the service where it matters.
        public bool CanAct { get; set; }
    }

    public class RegisteredUnitDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public int ModelID { get; set; }
        public AssetStatus Status { get; set; }
    }

    public class NewItemRequestStepDTO
    {
        public NewItemStatus Status { get; set; }
        public string ActedByName { get; set; } = null!;
        public string ActedByRole { get; set; } = null!;
        public DateTime ActedAt { get; set; }
        public string? Remarks { get; set; }
    }

    public class NewItemRequestItemDTO
    {
        public int NewItemRequestItemID { get; set; }

        public string ItemName { get; set; } = null!;
        public int Quantity { get; set; }

        // Brand, size, model, colour: whatever the requester wrote.
        public string? Description { get; set; }

        // Supplies only: ream, box, bottle.
        public string? Unit { get; set; }

        // The existing model or supply this line asks for more of, if one
        // was picked; otherwise the item is new to the school.
        public int? ModelID { get; set; }
        public int? SupplyID { get; set; }
        public bool IsExisting => ModelID.HasValue || SupplyID.HasValue;

        // Supplies requests: how many were added to stock for this request
        // so far (Scripts/028).
        public int AddedToStock { get; set; }
    }
}
