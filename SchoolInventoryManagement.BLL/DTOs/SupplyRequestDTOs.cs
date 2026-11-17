using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // A supply as the request form lists it: what the requester's own
    // branch has, and (for information) what the other branches have.
    public class SupplyRequestChoiceDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int LowStockLevel { get; set; }
        public int HereQuantity { get; set; }
        public int ElsewhereQuantity { get; set; }
    }

    public class CreateSupplyRequestDTO
    {
        [Display(Name = "Needed by")]
        public DateTime? NeededBy { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // Where the supplies are brought: a location in the requester's
        // own branch.
        [Required(ErrorMessage = "Pick where the supplies should be delivered.")]
        [Display(Name = "Deliver to")]
        public int? DeliverToLocationID { get; set; }

        public List<SupplyRequestLineDTO> Items { get; set; } = new();
    }

    public class SupplyRequestLineDTO
    {
        public int? SupplyID { get; set; }
        public int Quantity { get; set; } = 1;
    }

    // What an approver needs beside each line of a pending request.
    public class SupplyApprovalLineDTO
    {
        public int SupplyID { get; set; }
        // In the requester's branch, every room together.
        public int BranchQuantity { get; set; }
        // Already promised to other approved requests not yet released.
        public int HeldQuantity { get; set; }
        public int AvailableQuantity => Math.Max(0, BranchQuantity - HeldQuantity);
        public int ElsewhereQuantity { get; set; }
        public int LowStockLevel { get; set; }
    }

    public class SupplyApprovalInfoDTO
    {
        public List<SupplyApprovalLineDTO> Lines { get; set; } = new();
        // Rooms in the requester's branch.
        public List<SupplyRoomDTO> PickupLocations { get; set; } = new();
    }

    public class SupplyRoomDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        // Only on the release screen: how many of the supply are there.
        public int Quantity { get; set; }
    }

    public class ApproveSupplyRequestDTO
    {
        public List<SupplyRequestLineDTO> Items { get; set; } = new();

        // Only for an older request that has no deliver-to location.
        public int? PickupLocationID { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    // The release screen: for each supply, the rooms in the branch that
    // hold it.
    public class SupplyReleaseInfoDTO
    {
        public Dictionary<int, List<SupplyRoomDTO>> RoomsBySupply { get; set; } = new();
    }

    public class ReleaseSupplyRequestDTO
    {
        // One per supply per room it is taken from. Who received the
        // supplies is asked later, when they are delivered.
        public List<SupplyReleaseLineDTO> Lines { get; set; } = new();
    }

    public class SupplyReleaseLineDTO
    {
        public int SupplyID { get; set; }
        public int LocationID { get; set; }
        public int Quantity { get; set; }
    }

    // A supply request, for the list and for its own page.
    public class SupplyRequestResponseDTO
    {
        public int SupplyRequestID { get; set; }
        public SupplyRequestStatus Status { get; set; }
        public int RequestedByUserID { get; set; }
        public string RequestedByName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public DateTime? NeededBy { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }

        public string? DecidedByName { get; set; }
        public DateTime? DecidedAt { get; set; }
        public string? DecisionNote { get; set; }
        public string? PickupLocationName { get; set; }

        // Chosen by the requester. Older requests have a pick-up location
        // set by the approver instead; WhereName is whichever there is.
        public string? DeliverToLocationName { get; set; }
        public string? WhereName => DeliverToLocationName ?? PickupLocationName;
        public bool IsDelivery => DeliverToLocationName is not null;
        public string? ReleasedByName { get; set; }
        public DateTime? ReleasedAt { get; set; }
        public string? DeliveredByName { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public string? ReceivedByName { get; set; }
        // The last delivery that could not be made, while it is still out.
        public string? DeliveryProblem { get; set; }
        public DateTime? DeliveryProblemAt { get; set; }
        public DateTime? CancelledAt { get; set; }

        public string RequestedByRoleName { get; set; } = "";
        public int BranchID { get; set; }

        public List<SupplyRequestItemDTO> Items { get; set; } = new();

        public int TotalRequested => Items.Sum(i => i.QuantityRequested);
        // What will be, or was, handed over: the approved amounts.
        public int TotalApproved => Items.Sum(i => i.QuantityApproved ?? 0);

        // "Bond paper A4 x5, Whiteboard marker x2"
        public string ItemsSummary => string.Join(", ", Items.Select(i => $"{i.SupplyName} x{i.QuantityRequested}"));
    }

    public class SupplyRequestItemDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int QuantityRequested { get; set; }
        public int? QuantityApproved { get; set; }

        // Only on the Approvals list: what the requester's branch has of
        // it that is not already promised to another approved request.
        public int? AvailableQuantity { get; set; }
    }
}
