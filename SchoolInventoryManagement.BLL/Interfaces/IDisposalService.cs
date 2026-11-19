using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IDisposalService
    {
        Task<DisposalRecordResponseDTO> DisposeAssetAsync(int assetId, DisposeAssetDTO dto, int actingUserId);
        Task RestoreAssetAsync(int assetId, byte[] assetRowVersion, int actingUserId);

        Task<List<DisposalRecordResponseDTO>> GetDisposalHistoryForAssetAsync(int assetId);

        // Red tags: the Property Department marks a unit for disposal (a
        // reason is required); only a red-tagged unit can be disposed of.
        // Removing the tag puts it back in Property's stock. Asset
        // Officers and Administrators only.
        Task RedTagAsync(int assetId, string reason, byte[] assetRowVersion, int actingUserId);
        Task RemoveRedTagAsync(int assetId, byte[] assetRowVersion, int actingUserId);
        Task<List<RedTaggedAssetDTO>> GetRedTaggedAsync();
    }
}