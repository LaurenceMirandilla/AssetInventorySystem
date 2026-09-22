using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssignAssetViewModel
    {
        public int AssetID { get; set; }

        [Required]
        public int AssignToUserID { get; set; }

        [Required]
        public int DepartmentID { get; set; }


        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; } = ConditionStatus.Good;

        [MaxLength(500)]
        public string? Remarks { get; set; }
    }
}