using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAssetAssignmentService
    {
        // notifyRecipient lets RequestFulfillmentService suppress the
        // per-step notification in favour of one combined message.
        // requestId links the assignment to the request the unit went out
        // on. keptAtLocationId is the room the unit will be kept in while
        // it is assigned (a fan for a classroom); null means it is simply
        // with the person. branchId moves the asset to that branch (the
        // department and any location must then be in it); null keeps its
        // branch. All optional, so existing call sites are unaffected.
        Task<AssetAssignmentResponseDTO> AssignAssetAsync(
            int assetId, int assignToUserId, ConditionStatus conditionOnAssignment,
            int departmentId, int actingUserId, string? remarks, bool notifyRecipient = true,
            int? requestId = null, int? keptAtLocationId = null, int? branchId = null);

        // Closes the assignment and puts the unit at returnLocationId (staff
        // choose it). If the unit was out on a request and was its last one
        // still out, that request is marked Returned in the same save.
        Task ReturnAssetAsync(
            int assignmentId, ConditionStatus conditionOnReturn, int returnLocationId,
            byte[] rowVersion, int actingUserId);

        Task<AssetAssignmentResponseDTO?> GetAssignmentByIdAsync(int assignmentId);
        Task<List<AssetAssignmentResponseDTO>> GetAssignmentHistoryForAssetAsync(int assetId);
        Task<List<AssetAssignmentResponseDTO>> GetActiveAssignmentsForUserAsync(int userId);

        // What they held before and gave back (or was handed on for good),
        // newest first. For the Users page's "Past items" tab.
        Task<List<AssetAssignmentResponseDTO>> GetPastAssignmentsForUserAsync(int userId);
    }
}