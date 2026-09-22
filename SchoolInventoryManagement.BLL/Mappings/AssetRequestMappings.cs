using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetRequestMappings
    {
        // Requires request.RequestedByUser(.Role), request.Department,
        // request.Model, request.Asset, request.RequestedLocation,
        // request.ApprovedByUser(.Role) to be loaded as applicable
        public static AssetRequestResponseDTO ToResponseDTO(this AssetRequest request)
        {
            return new AssetRequestResponseDTO
            {
                RequestID = request.RequestID,
                RequestedByUser = request.RequestedByUser.ToSummaryDTO(),
                DepartmentID = request.DepartmentID,
                DepartmentName = request.Department.DepartmentName,
                ModelID = request.ModelID,
                ModelName = request.Model?.ModelName,
                AssetID = request.AssetID,
                AssetCode = request.Asset?.AssetCode,
                RequestedLocationID = request.RequestedLocationID,
                RequestedLocationName = request.RequestedLocation?.LocationName,
                RequestType = request.RequestType,
                RequestDate = request.RequestDate,
                Reason = request.Reason,
                RequestStatus = request.RequestStatus,
                ApprovedByUser = request.ApprovedByUser?.ToSummaryDTO(),
                ApprovalDate = request.ApprovalDate,
                Remarks = request.Remarks,
                RowVersion = request.RowVersion
            };
        }
    }
}