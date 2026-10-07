using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Approvals > Request History. The filters come in on the query
    // string; the rest is filled by the controller.
    public class RequestHistoryViewModel
    {
        public const int PageSize = 20;

        // "Approved" or "Rejected"; empty means both.
        public string? Decision { get; set; }
        public RequestType? Type { get; set; }
        public string? Keyword { get; set; }
        public int? DecidedBy { get; set; }
        public int? DepartmentId { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;

        [BindNever] public List<AssetRequestResponseDTO> Rows { get; set; } = new();
        [BindNever] public int TotalCount { get; set; }
        [BindNever] public int ApprovedCount { get; set; }
        [BindNever] public int RejectedCount { get; set; }
        [BindNever] public List<UserSummaryDTO> Deciders { get; set; } = new();
        [BindNever] public List<DepartmentDTO> Departments { get; set; } = new();

        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));

        public bool HasFilters =>
            !string.IsNullOrEmpty(Decision) || Type.HasValue || !string.IsNullOrWhiteSpace(Keyword) ||
            DecidedBy.HasValue || DepartmentId.HasValue || FromDate.HasValue || ToDate.HasValue;

        // The current filters as route values, for the tiles and the pager.
        // Decision and page are left for the caller to set.
        public Dictionary<string, string> FilterRoute()
        {
            var route = new Dictionary<string, string>();
            if (Type.HasValue) route["type"] = Type.Value.ToString();
            if (!string.IsNullOrWhiteSpace(Keyword)) route["keyword"] = Keyword;
            if (DecidedBy.HasValue) route["decidedBy"] = DecidedBy.Value.ToString();
            if (DepartmentId.HasValue) route["departmentId"] = DepartmentId.Value.ToString();
            if (FromDate.HasValue) route["fromDate"] = FromDate.Value.ToString("yyyy-MM-dd");
            if (ToDate.HasValue) route["toDate"] = ToDate.Value.ToString("yyyy-MM-dd");
            return route;
        }
    }
}
