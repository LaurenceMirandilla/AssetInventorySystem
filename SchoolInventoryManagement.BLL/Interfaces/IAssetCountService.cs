using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Physical counts of assets, one room at a time, by the Property
    // Department (Property Officers and Administrators). The Principal can
    // read past counts.
    public interface IAssetCountService
    {
        Task<List<AssetCountRoomDTO>> GetRoomsAsync(int actingUserId);
        Task<AssetCountSheetDTO?> GetSheetAsync(int locationId, int actingUserId);

        // Saves the count and returns its id. Units on the sheet not ticked
        // are recorded missing; extra codes are units found there that the
        // system had elsewhere (moved here when asked, where they can be).
        Task<(int CountId, int Moved, List<string> NotMoved)> SaveAsync(SaveAssetCountDTO dto, int actingUserId);

        Task<List<AssetCountSummaryDTO>> GetCountsAsync(int actingUserId);
        Task<AssetCountDetailDTO?> GetCountAsync(int countId, int actingUserId);
        Task<AssetLastCountDTO?> GetLastCountForAssetAsync(int assetId);
    }
}
