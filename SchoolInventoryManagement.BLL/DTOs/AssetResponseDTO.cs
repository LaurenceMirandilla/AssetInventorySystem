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
        public string? WarrantyFileURL { get; set; }
        public string? ImageURL { get; set; }
        public string? BarcodeData { get; set; }
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

        // The new-item request it was bought for, if any. While it is In
        // transit with no holder it is held by Property for that request,
        // until the requester confirms receipt.
        public int? NewItemRequestID { get; set; }

        // Assigned to a department with no person holding it
        // (AssetAssignmentService.AssignToDepartmentAsync): it belongs to
        // that department and cannot be borrowed.
        public bool IsWithDepartment =>
            Status == AssetStatus.Assigned && AssignedUserID == null && ActiveAssignmentID == null;

        // The red tag: why it was marked for disposal, by whom, when.
        public string? RedTagReason { get; set; }
        public DateTime? RedTaggedAt { get; set; }
        public string? RedTaggedByName { get; set; }

        // What the Property Department can red-tag: its own stock, a unit
        // under maintenance, or one issued to a department (taken back).
        // Not one that is out with a person or on a request.
        public bool CanBeRedTagged =>
            Status is AssetStatus.Available or AssetStatus.UnderMaintenance || IsWithDepartment;

        public byte[] RowVersion { get; set; } = null!;
    }
}