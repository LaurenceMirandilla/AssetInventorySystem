using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // "Assign to a department": one unit (AssetID), or a number of units
    // of a model. No person is involved; the units belong to the
    // department and can no longer be borrowed.
    public class AssignToDepartmentViewModel
    {
        public int? AssetID { get; set; }

        public int? ModelID { get; set; }

        [Range(1, 1000, ErrorMessage = "Enter how many to assign: from 1 to 1000 at a time.")]
        public int Quantity { get; set; } = 1;

        // Only take units that are in this room now; empty = from anywhere.
        public int? FromLocationID { get; set; }

        [Required(ErrorMessage = "Pick a department.")]
        public int? DepartmentID { get; set; }

        [Required(ErrorMessage = "Pick the location the assets are placed in.")]
        public int? LocationID { get; set; }

        [MaxLength(500)]
        public string? Remarks { get; set; }

        // Where the model's other available units go; empty = stay put.
        public int? MoveRestToLocationID { get; set; }
    }
}
