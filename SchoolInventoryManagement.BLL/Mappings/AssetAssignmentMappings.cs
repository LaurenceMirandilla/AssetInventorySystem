using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetAssignmentMappings
    {
        // Requires assignment.Asset, assignment.AssignedToUser(.Role),
        // assignment.AssignedByUser(.Role) to be loaded
        public static AssetAssignmentResponseDTO ToResponseDTO(this AssetAssignment assignment)
        {
            return new AssetAssignmentResponseDTO
            {
                AssignmentID = assignment.AssignmentID,
                AssetID = assignment.AssetID,
                AssetCode = assignment.Asset.AssetCode,
                AssetName = assignment.Asset.AssetName,
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