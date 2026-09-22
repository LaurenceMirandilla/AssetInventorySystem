using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Borrow approval is where the abstract request becomes a physical
    // unit: staff pick which AssetID of the requested Model actually goes
    // out, and confirm the department it is being issued to.
    public class ApproveBorrowRequestViewModel
    {
        public int RequestID { get; set; }

        [Required(ErrorMessage = "Pick which unit to hand over.")]
        public int AssetID { get; set; }

        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; } = ConditionStatus.Good;

        [Required]
        public int DepartmentID { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}
