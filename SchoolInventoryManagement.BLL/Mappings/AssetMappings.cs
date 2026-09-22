using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;
using System.Linq;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetMappings
    {
        // Requires asset.Model, asset.Model.Category, asset.CurrentLocation,
        // asset.AssignedUser, asset.Department, and asset.Branch to already
        // be loaded via .Include() — this method does NOT lazy-load them.
        public static AssetResponseDTO ToResponseDTO(this Asset asset)
        {
            return new AssetResponseDTO
            {
                AssetID = asset.AssetID,
                AssetCode = asset.AssetCode,
                AssetName = asset.AssetName,
                Description = asset.Description,
                SerialNumber = asset.SerialNumber,
                AcquisitionDate = asset.AcquisitionDate,
                AcquisitionCost = asset.AcquisitionCost,
                WarrantyInformation = asset.WarrantyInformation,
                ImageURL = asset.ImageURL,
                QRCodeData = asset.QRCodeData,
                Condition = asset.Condition,
                Status = asset.Status,
                ModelID = asset.ModelID,
                ModelName = asset.Model.ModelName,
                CategoryName = asset.Model.Category.CategoryName,
                CurrentLocationID = asset.CurrentLocationID,
                CurrentLocationName = asset.CurrentLocation?.LocationName,
                AssignedUserID = asset.AssignedUserID,
                AssignedUserName = asset.AssignedUser is null
                    ? null
                    : $"{asset.AssignedUser.FirstName} {asset.AssignedUser.LastName}",
                DepartmentID = asset.DepartmentID,
                DepartmentName = asset.Department?.DepartmentName,
                BranchID = asset.BranchID,
                BranchName = asset.Branch.BranchName,
                ActiveAssignmentID = asset.AssetAssignments
            .Where(a => a.ReturnDate == null)
            .Select(a => (int?)a.AssignmentID)
            .FirstOrDefault(),
                RowVersion = asset.RowVersion,
            };
        }
    }
}