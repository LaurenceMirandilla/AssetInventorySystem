using System;
using System.Collections.Generic;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // Filters for the decided-requests history (Approvals > Request
    // History). Every one is optional.
    public class RequestHistoryFilterDTO
    {
        // "Approved" or "Rejected"; anything else means both.
        public string? Decision { get; set; }
        public RequestType? Type { get; set; }

        // Request number, requester name, or a model on the request.
        public string? Keyword { get; set; }

        public int? DecidedByUserId { get; set; }
        public int? DepartmentId { get; set; }

        // On the decision date, inclusive on both ends.
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class RequestHistoryResultDTO
    {
        // One page of requests, newest decision first.
        public List<AssetRequestResponseDTO> Rows { get; set; } = new();

        // Across every page of the filtered set.
        public int TotalCount { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }

        // Everyone who has decided a request, for the "Decided by" filter.
        public List<UserSummaryDTO> Deciders { get; set; } = new();
    }
}
