using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAssetService
    {
        Task<AssetResponseDTO> CreateAssetAsync(CreateAssetDTO dto, int actingUserId);

        // Registers dto.Quantity units of one model in a single save and
        // returns their codes in order. All or nothing.
        Task<List<string>> BulkCreateAssetsAsync(BulkCreateAssetsDTO dto, int actingUserId);
        Task<AssetResponseDTO?> GetAssetByIdAsync(int assetId);
        Task<List<AssetResponseDTO>> GetAllAssetsAsync();

        // locationId is last and optional so the existing callers (request
        // approval screens, QR scan) compile unchanged.
        Task<List<AssetResponseDTO>> SearchAssetsAsync(
            string? keyword, int? categoryId, int? modelId, int? branchId,
            int? departmentId, AssetStatus? status, ConditionStatus? condition,
            int? locationId = null);

        Task UpdateAssetAsync(int assetId, UpdateAssetDTO dto, int actingUserId);
        Task ChangeConditionAsync(int assetId, ConditionStatus newCondition, byte[] rowVersion, int actingUserId);
        Task ChangeStatusAsync(int assetId, AssetStatus newStatus, byte[] rowVersion, int actingUserId);
    }
}