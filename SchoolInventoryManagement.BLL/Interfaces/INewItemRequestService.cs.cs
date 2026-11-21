using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface INewItemRequestService
    {
        // One ticket with one or more items, approved or rejected together.
        // Only people who have been allowed to request new items.
        Task<NewItemRequestResponseDTO> CreateRequestAsync(CreateNewItemRequestDTO dto, int actingUserId);

        // ---- Who can request new items ----

        // Whether this user has been allowed to file new-item requests.
        Task<bool> CanRequestAsync(int userId);

        // The people in the user's own department who are allowed, by
        // name: who to ask when you are not one of them.
        Task<List<string>> GetDepartmentRequesterNamesAsync(int userId);

        // Every department with its active people and whether each may
        // request. Administrator, Principal and Property Officer.
        Task<List<RequesterDepartmentDTO>> GetRequesterDirectoryAsync(int actingUserId);

        // Turns a person's permission on or off. Administrator, Principal
        // and Property Officer.
        Task SetCanRequestAsync(int userId, bool allowed, int actingUserId);

        // The existing models, supplies and locations the form searches.
        Task<NewItemFormLookupsDTO> GetFormLookupsAsync(int actingUserId);

        Task<List<NewItemRequestResponseDTO>> GetMyRequestsAsync(int actingUserId);

        // The requests waiting on the acting user: new tickets for the
        // Principal to approve; procurement and arrival for Asset
        // Officers; all of these for Administrators.
        // Approved purchases to buy, and bought ones waiting to arrive.
        Task<List<NewItemRequestResponseDTO>> GetPurchasingQueueAsync(int actingUserId);
        Task<List<NewItemRequestResponseDTO>> GetPendingRequestsAsync(int actingUserId);

        // Open tickets the acting user can see that are not waiting on
        // them (New-Item Approvals' "In progress" tab).
        Task<List<NewItemRequestResponseDTO>> GetInProgressRequestsAsync(int actingUserId);

        // Every request from everyone, optionally one stage only, and/or
        // matching search (item, reason, requester or department). Ordered
        // by submission time, newest first unless oldestFirst. Asset
        // Officer, Administrator and Principal.
        Task<List<NewItemRequestResponseDTO>> GetAllRequestsAsync(
            int actingUserId, NewItemStatus? status, string? search = null, bool oldestFirst = false);

        // One request with its full step history. The requester, their
        // department's head, and the roles that see all requests.
        Task<NewItemRequestResponseDTO> GetRequestAsync(int requestId, int actingUserId);

        // Moves the request to its next stage: the Principal's approval ->
        // procuring -> waiting for arrival -> arrived -> delivered (all but
        // the first by a Property Officer). Who may do it depends on the stage.
        // Returns the stage it moved to, so the page can say what happened.
        Task<SchoolInventoryManagement.DAL.Entities.Enums.NewItemStatus> AdvanceRequestAsync(
            int requestId, byte[] rowVersion, int actingUserId, string? remarks);

        // At any open stage, by whoever holds it: the Principal before
        // procurement, a Property Officer during it. A reason is required.
        Task RejectRequestAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);

        // At the Delivery stage: the delivery could not be made. A reason
        // is required. The ticket stays where it is, to be tried again.
        Task MarkNotDeliveredAsync(int requestId, byte[] rowVersion, int actingUserId, string? remarks);

        // Delivered assets: the requester confirms receiving them. The units
        // registered for the request become their department's (Property:
        // into stock). Only the requester.
        Task AcknowledgeReceiptAsync(int requestId, byte[] rowVersion, int actingUserId);

        // Requisitions (Scripts/031): units Property picks from stock, and
        // sending what is short in stock back to Purchasing.
        Task<PickUnitsDTO> GetPickAsync(int requestId, int actingUserId);
        Task<int> SetPickedUnitsAsync(int requestId, Dictionary<int, List<int>> picks, int actingUserId);
        Task<string> SendShortToPurchasingAsync(int requestId, byte[] rowVersion, int actingUserId);
    }
}