using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Borrow approval is where the abstract request becomes a physical
    // unit: staff pick which AssetID of the requested Model goes out,
    // confirm the department it is issued to, and say where the requester
    // can collect it. The request is then In Transit until they do.
    public class ApproveBorrowRequestViewModel
    {
        public int RequestID { get; set; }

        [Required(ErrorMessage = "Pick which unit to hand over.")]
        public int AssetID { get; set; }

        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; } = ConditionStatus.Good;

        [Required]
        public int DepartmentID { get; set; }

        [Required(ErrorMessage = "Say where the requester can collect it.")]
        [Display(Name = "Pickup location")]
        public int? PickupLocationID { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}