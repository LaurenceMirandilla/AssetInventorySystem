using System.Linq;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class AssetRequestMappings
    {
        // Requires request.RequestedByUser(.Role), request.Department,
        // request.Items(.Model), request.Assignments(.Asset),
        // request.Model, request.Asset, request.RequestedLocation,
        // request.PickupLocation and request.ApprovedByUser(.Role) to be
        // loaded as applicable -- AssetRequestService.RequestQueryWithIncludes
        // does all of it.
        public static AssetRequestResponseDTO ToResponseDTO(this AssetRequest request)
        {
            var items = request.Items
                .OrderBy(i => i.Model?.ModelName)
                .Select(i => new AssetRequestItemDTO
                {
                    RequestItemID = i.RequestItemID,
                    ModelID = i.ModelID,
                    ModelName = i.Model?.ModelName ?? "(model missing)",
                    Quantity = i.Quantity
                })
                .ToList();

            var units = request.Assignments
                .OrderBy(a => a.Asset?.AssetCode)
                .Select(a => new AssetRequestUnitDTO
                {
                    AssignmentID = a.AssignmentID,
                    AssetID = a.AssetID,
                    AssetCode = a.Asset?.AssetCode ?? "(asset missing)",
                    AssetName = a.Asset?.AssetName ?? "",
                    ModelID = a.Asset?.ModelID ?? 0,
                    AssetStatus = a.Asset?.Status ?? default,
                    ReturnDate = a.ReturnDate,
                    ConditionOnReturn = a.ConditionOnReturn
                })
                .ToList();

            // Lines first; an old row with none falls back to what it named.
            var summary = items.Count > 0
                ? string.Join(", ", items.Select(i => i.Quantity > 1 ? $"{i.ModelName} ×{i.Quantity}" : i.ModelName))
                : request.Model?.ModelName ?? request.Asset?.AssetCode ?? "—";

            return new AssetRequestResponseDTO
            {
                RequestID = request.RequestID,
                RequestedByUser = request.RequestedByUser.ToSummaryDTO(),
                DepartmentID = request.DepartmentID,
                DepartmentName = request.Department.DepartmentName,
                Items = items,
                Units = units,
                ItemsSummary = summary,
                ModelID = request.ModelID,
                ModelName = request.Model?.ModelName,
                AssetID = request.AssetID,
                AssetCode = request.Asset?.AssetCode,
                RequestedLocationID = request.RequestedLocationID,
                RequestedLocationName = request.RequestedLocation?.LocationName,
                PickupLocationID = request.PickupLocationID,
                PickupLocationName = request.PickupLocation?.LocationName,
                RequestType = request.RequestType,
                RequestDate = request.RequestDate,
                Reason = request.Reason,
                RequestStatus = request.RequestStatus,
                NeededFrom = request.NeededFrom,
                ReturnBy = request.ReturnBy,
                AssignedDate = request.AssignedDate,
                ReturnedDate = request.ReturnedDate,
                ApprovedByUser = request.ApprovedByUser?.ToSummaryDTO(),
                ApprovalDate = request.ApprovalDate,
                Remarks = request.Remarks,
                RowVersion = request.RowVersion
            };
        }
    }
}