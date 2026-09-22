using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AssetResponseDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string? Description { get; set; }
        public string? SerialNumber { get; set; }
        public DateTime? AcquisitionDate { get; set; }
        public decimal? AcquisitionCost { get; set; }
        public string? WarrantyInformation { get; set; }
        public string? ImageURL { get; set; }
        public string? QRCodeData { get; set; }
        public ConditionStatus Condition { get; set; }
        public AssetStatus Status { get; set; }

        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;

        public int? CurrentLocationID { get; set; }
        public string? CurrentLocationName { get; set; }
        public int? AssignedUserID { get; set; }
        public string? AssignedUserName { get; set; }
        public int? DepartmentID { get; set; }
        public string? DepartmentName { get; set; }
        public int? BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public int? ActiveAssignmentID { get; set; }

        public byte[] RowVersion { get; set; } = null!;
    }
}