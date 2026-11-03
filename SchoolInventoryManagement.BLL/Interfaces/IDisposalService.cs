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
    }
}