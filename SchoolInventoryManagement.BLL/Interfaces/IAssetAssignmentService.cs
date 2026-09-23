using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAssetAssignmentService
    {
        // notifyRecipient lets RequestFulfillmentService suppress the
        // per-step notification in favour of one combined message. Optional,
        // so existing call sites are unaffected.
        Task<AssetAssignmentResponseDTO> AssignAssetAsync(
    int assetId, int assignToUserId, ConditionStatus conditionOnAssignment,
    int departmentId, int actingUserId, string? remarks, bool notifyRecipient = true);

        // Closes the assignment and puts the unit at returnLocationId (staff
        // choose it). If the unit was out on a request, that request is
        // marked Returned in the same save.
        Task ReturnAssetAsync(
            int assignmentId, ConditionStatus conditionOnReturn, int returnLocationId,
            byte[] rowVersion, int actingUserId);

        Task<AssetAssignmentResponseDTO?> GetAssignmentByIdAsync(int assignmentId);
        Task<List<AssetAssignmentResponseDTO>> GetAssignmentHistoryForAssetAsync(int assetId);
        Task<List<AssetAssignmentResponseDTO>> GetActiveAssignmentsForUserAsync(int userId);
    }
}