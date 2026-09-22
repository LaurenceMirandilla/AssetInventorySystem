using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAssetMovementService
    {
        Task<AssetMovementResponseDTO> TransferAssetAsync(
            int assetId, int destinationLocationId, string reasonForTransfer,
            ConditionStatus? conditionOnTransfer, int actingUserId, string? notes);

        Task<AssetMovementResponseDTO?> GetMovementByIdAsync(int movementId);
        Task<List<AssetMovementResponseDTO>> GetMovementHistoryForAssetAsync(int assetId);
        Task<List<AssetMovementResponseDTO>> GetMovementHistoryForLocationAsync(int locationId);
    }
}