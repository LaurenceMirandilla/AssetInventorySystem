using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Keeps open pages up to date without a reload: counts for the sidebar,
    // and a stamp per page that changes when its data changes.
    //
    // scope says what a page shows:
    //   approvals, newitems, history, mine, assets, supplies,
    //   notifications, home
    //   assetrequest:5, supplyrequest:5, newitem:5, asset:5, supply:5
    public interface ILiveStatusService
    {
        Task<LiveStatusDTO> GetStatusAsync(int userId, string? scope);

        // The stamp alone, written into the page when it is drawn.
        Task<string?> GetStampAsync(int userId, string? scope);

        // The newest notification id this person has, read or not. The
        // page starts from it, so only newer ones pop up.
        Task<int> GetLatestNotificationIdAsync(int userId);

        // Requests waiting for this person's decision: the same set the
        // Approvals page lists. 0 for roles that do not approve.
        Task<int> CountApprovalsAsync(int userId);
    }
}
