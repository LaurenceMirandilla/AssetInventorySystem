using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetAssignmentMappings
    {
        // Requires assignment.Asset, assignment.AssignedToUser(.Role),
        // assignment.AssignedByUser(.Role) to be loaded. assignment.Request
        // is optional; without it the request fields stay empty.
        public static AssetAssignmentResponseDTO ToResponseDTO(this AssetAssignment assignment)
        {
            return new AssetAssignmentResponseDTO
            {
                AssignmentID = assignment.AssignmentID,
                AssetID = assignment.AssetID,
                AssetCode = assignment.Asset.AssetCode,
                AssetName = assignment.Asset.AssetName,
                ModelName = assignment.Asset.Model?.ModelName ?? assignment.Asset.AssetName,
                AssetStatus = assignment.Asset.Status,
                RequestID = assignment.RequestID,
                RequestType = assignment.Request?.RequestType,
                RequestReturnBy = assignment.Request?.ReturnBy,
                AssignedToUser = assignment.AssignedToUser.ToSummaryDTO(),
                AssignedByUser = assignment.AssignedByUser.ToSummaryDTO(),
                AssignmentDate = assignment.AssignmentDate,
                ConditionOnAssignment = assignment.ConditionOnAssignment,
                ReturnDate = assignment.ReturnDate,
                ConditionOnReturn = assignment.ConditionOnReturn,
                Remarks = assignment.Remarks,
                RowVersion = assignment.RowVersion
            };
        }
    }
}