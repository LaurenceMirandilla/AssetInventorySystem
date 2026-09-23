using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class NewItemRequestMappings
    {
        // Assumes the caller loaded RequestedByUser, Department and
        // ReviewedByUser -- NewItemRequestService.QueryWithIncludes() is
        // the only thing that builds these.
        public static NewItemRequestResponseDTO ToResponseDTO(this NewItemRequest request)
        {
            return new NewItemRequestResponseDTO
            {
                NewItemRequestID = request.NewItemRequestID,
                RequestedByUserID = request.RequestedByUserID,
                RequestedByUserName =
                    $"{request.RequestedByUser.FirstName} {request.RequestedByUser.LastName}",
                DepartmentName = request.Department.DepartmentName,
                ItemName = request.ItemName,
                Reason = request.Reason,
                NeededBy = request.NeededBy,
                RequestDate = request.RequestDate,
                RequestStatus = request.RequestStatus,
                ReviewedByUserName = request.ReviewedByUser is null
                    ? null
                    : $"{request.ReviewedByUser.FirstName} {request.ReviewedByUser.LastName}",
                ReviewDate = request.ReviewDate,
                Remarks = request.Remarks,
                RowVersion = request.RowVersion
            };
        }
    }
}