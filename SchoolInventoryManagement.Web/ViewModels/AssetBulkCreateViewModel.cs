using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Codes are generated from the model's category prefix, carrying on
    // from the highest number already used -- nobody types a prefix or a
    // starting number.
    public class AssetBulkCreateViewModel
    {
        [Required(ErrorMessage = "Pick a model from the list.")]
        [Display(Name = "Model")]
        public int? ModelID { get; set; }

        [Range(1, 200, ErrorMessage = "Register between 1 and 200 at a time.")]
        [Display(Name = "How many")]
        public int Quantity { get; set; } = 10;

        [Required(ErrorMessage = "Give the units a name.")]
        [MaxLength(130)]
        [Display(Name = "Name")]
        public string BaseName { get; set; } = null!;

        [Display(Name = "Serial numbers")]
        public string? SerialNumbers { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Display(Name = "Acquisition date")]
        public DateTime? AcquisitionDate { get; set; }

        [Range(0, 99999999)]
        [Display(Name = "Cost per unit")]
        public decimal? AcquisitionCost { get; set; }

        [MaxLength(255)]
        [Display(Name = "Warranty")]
        public string? WarrantyInformation { get; set; }

        // One file for the whole batch -- the delivery's warranty card or
        // receipt -- linked from every unit.
        [Display(Name = "Warranty photo or file")]
        public IFormFile? WarrantyFile { get; set; }

        public ConditionStatus Condition { get; set; } = ConditionStatus.New;

        [Display(Name = "Location")]
        public int? CurrentLocationID { get; set; }

        // Only the chosen branch's departments are offered.
        [Required(ErrorMessage = "Pick a department.")]
        [Display(Name = "Department")]
        public int? DepartmentID { get; set; }

        [Required(ErrorMessage = "Pick a branch.")]
        [Display(Name = "Branch")]
        public int? BranchID { get; set; }

        [Display(Name = "Add barcode")]
        public bool CreateBarcode { get; set; }

        // Registering the units bought for this new-item request.
        public int? NewItemRequestID { get; set; }
    }

    // The new-item request whose units are being registered: shown at the
    // top of Bulk register, with what is still to be registered.
    public class BulkTicketInfo
    {
        public int RequestID { get; set; }
        public string RequesterName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public int PropertyDepartmentID { get; set; }
        public int Ordered { get; set; }
        public int Registered { get; set; }
        public List<BulkTicketLine> Lines { get; set; } = new();
    }

    public class BulkTicketLine
    {
        public string ItemName { get; set; } = null!;
        public int? ModelID { get; set; }
        public int Quantity { get; set; }
        public decimal? EstimatedUnitPrice { get; set; }
        public int Registered { get; set; }
    }
}