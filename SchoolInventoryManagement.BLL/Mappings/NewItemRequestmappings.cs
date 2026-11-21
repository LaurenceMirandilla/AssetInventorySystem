using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class NewItemRequestMappings
    {
        // Assumes the caller loaded RequestedByUser, Department, Items,
        // ReviewedByUser and DeliverToLocation with its Branch --
        // NewItemRequestService.QueryWithIncludes() is the only thing that
        // builds these.
        public static NewItemRequestResponseDTO ToResponseDTO(this NewItemRequest request)
        {
            return new NewItemRequestResponseDTO
            {
                NewItemRequestID = request.NewItemRequestID,
                RequestedByUserID = request.RequestedByUserID,
                RequestedByUserName =
                    $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}",
                RequestedByRoleName = request.RequestedByUser.Role?.RoleName,
                DepartmentID = request.DepartmentID,
                DepartmentName = request.Department.DepartmentName,
                ItemName = request.ItemName,
                Quantity = request.Quantity,
                ItemKind = request.ItemKind,
                DeliverToLocationID = request.DeliverToLocationID,
                DeliverToLocationName = request.DeliverToLocation?.LocationName,
                DeliverToBranchName = request.DeliverToLocation?.Branch?.BranchName,
                // A ticket's lines. Every request has at least one once
                // Scripts/014 has run (it copies older requests in as one-item
                // tickets), so nothing is made up here: a query that forgets
                // to include Items shows an empty ticket instead of a wrong one.
                Items = request.Items
                    .OrderBy(i => i.NewItemRequestItemID)
                    .Select(i => new NewItemRequestItemDTO
                    {
                        NewItemRequestItemID = i.NewItemRequestItemID,
                        ItemName = i.ItemName,
                        Quantity = i.Quantity,
                        Description = i.Description,
                        Unit = i.Unit,
                        ModelID = i.ModelID,
                        SupplyID = i.SupplyID,
                        ToBuy = i.ToBuy
                    })
                    .ToList(),
                Reason = request.Reason,
                NeededBy = request.NeededBy,
                RequestDate = request.RequestDate,
                RequestStatus = request.RequestStatus,
                ReviewedByUserName = request.ReviewedByUser is null
                    ? null
                    : $"{request.ReviewedByUser.FirstName} {request.ReviewedByUser.LastName}",
                ReviewDate = request.ReviewDate,
                Remarks = request.Remarks,
                AcknowledgedAt = request.AcknowledgedAt,
                RowVersion = request.RowVersion,
                Steps = request.Steps
                    .OrderBy(s => s.ActedAt).ThenBy(s => s.StepID)
                    .Select(s => new NewItemRequestStepDTO
                    {
                        Status = s.Status,
                        ActedByName = s.ActedByUser is null
                            ? "(unknown)"
                            : $"{s.ActedByUser.FirstName} {s.ActedByUser.LastName}",
                        ActedByRole = s.ActedByUser?.Role?.RoleName ?? "",
                        ActedAt = s.ActedAt,
                        Remarks = s.Remarks
                    })
                    .ToList()
            };
        }
    }
}