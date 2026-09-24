using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Borrow approval is where the ticket becomes physical units: for every
    // line, staff tick exactly that many units of that model, confirm the
    // department they are issued to, and -- unless the requester already
    // named a pickup spot -- say where they can collect them. The request
    // is then In Transit until they do.
    public class ApproveBorrowRequestViewModel
    {
        public int RequestID { get; set; }

        // Every unit ticked, across all lines.
        public List<int> AssetIDs { get; set; } = new();

        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; } = ConditionStatus.Good;

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
        public List<SelectListItem> Units { get; set; } = new();
    }
}