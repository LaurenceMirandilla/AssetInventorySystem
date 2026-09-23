using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetRequestMappings
    {
        // Requires request.RequestedByUser(.Role), request.Department,
        // request.Model, request.Asset(.AssetAssignments),
        // request.RequestedLocation, request.PickupLocation and
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
                AssetName = request.Asset?.AssetName,
                RequestedLocationID = request.RequestedLocationID,
                RequestedLocationName = request.RequestedLocation?.LocationName,
                PickupLocationID = request.PickupLocationID,
                PickupLocationName = request.PickupLocation?.LocationName,
                // Only while this request is actually out -- once it is
                // Returned, the same unit may be lent to someone else, and
                // that assignment is not this request's to return.
                ActiveAssignmentID =
                    request.RequestStatus == RequestStatus.InTransit ||
                    request.RequestStatus == RequestStatus.Assigned
                        ? request.Asset?.AssetAssignments
                            .FirstOrDefault(a => a.ReturnDate == null)?.AssignmentID
                        : null,
                RequestType = request.RequestType,
                RequestDate = request.RequestDate,
                Reason = request.Reason,
                RequestStatus = request.RequestStatus,
                NeededFrom = request.NeededFrom,
                ReturnBy = request.ReturnBy,
                AssignedDate = request.AssignedDate,
                ReturnedDate = request.ReturnedDate,
                ApprovedByUser = request.ApprovedByUser?.ToSummaryDTO(),
                ApprovalDate = request.ApprovalDate,
                Remarks = request.Remarks,
                RowVersion = request.RowVersion
            };
        }
    }
}