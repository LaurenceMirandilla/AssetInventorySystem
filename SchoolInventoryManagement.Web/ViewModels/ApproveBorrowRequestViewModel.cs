using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Borrow approval is where the ticket becomes physical units: for every
    // line, staff tick exactly that many units of that model, confirm the
    // department they are issued to, and -- unless the requester already
    // named a pickup spot -- say where they can collect them. The request
    // is then In Transit until they do. No condition is asked for: each
    // unit is recorded as it stands.
    public class ApproveBorrowRequestViewModel
    {
        public int RequestID { get; set; }

        // Every unit ticked, across all lines.
        public List<int> AssetIDs { get; set; } = new();

        [Required]
        public int DepartmentID { get; set; }

        // Only asked for when the requester left their preferred pickup
        // location blank; the controller checks it in that case.
        [Display(Name = "Pickup location")]
        public int? PickupLocationID { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }

    // One line of the ticket on the approval page: the model, how many to
    // pick, and the units that can be picked. Used by Borrow and Transfer.
    public class ApprovalLineChoice
    {
        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public int Quantity { get; set; }
        public List<ApprovalUnitOption> Units { get; set; } = new();
    }

    // One Available unit on an approval line, with what the page's filters
    // and Auto-fill need: where it is (location and branch) and its
    // condition.
    public class ApprovalUnitOption
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public ConditionStatus Condition { get; set; }
        public int? LocationID { get; set; }
        public string? LocationName { get; set; }
        public int? BranchID { get; set; }
        public bool Selected { get; set; }
    }
}
