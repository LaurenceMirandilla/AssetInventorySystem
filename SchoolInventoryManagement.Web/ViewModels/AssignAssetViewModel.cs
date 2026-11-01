using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class AssignAssetViewModel
    {
        public int AssetID { get; set; }

        // Posted by a radio list, which sends nothing when no one is picked
        // -- so 0 means "not picked" and [Required] alone would let it by.
        [Range(1, int.MaxValue, ErrorMessage = "Pick who gets it.")]
        public int AssignToUserID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        // The branch it is going to; starts as the asset's own. Picking
        // another moves the asset there.
        [Required(ErrorMessage = "Pick a branch.")]
        public int? BranchID { get; set; }

        // The room it will be kept in while assigned; empty means it is
        // simply with the person.
        public int? LocationID { get; set; }


        [Required]
        public ConditionStatus ConditionOnAssignment { get; set; } = ConditionStatus.Good;

        [MaxLength(500)]
        public string? Remarks { get; set; }
    }

    // One person in the Assign form's "Assign to" list.
    public class AssignPersonOption
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = null!;
        public string RoleName { get; set; } = null!;
        public int DepartmentID { get; set; }
        public string DepartmentName { get; set; } = null!;
        public int BranchID { get; set; }
    }
}