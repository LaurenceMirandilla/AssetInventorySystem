using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class ReturnAssetViewModel
    {
        public int AssignmentID { get; set; }

        [Required]
        public ConditionStatus ConditionOnReturn { get; set; }

        [Required]
        public string RowVersionBase64 { get; set; } = null!;
    }
}