using System;
using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Drives Views/Assets/Index.cshtml. The KPI counts are computed from the
    // WHOLE inventory (ignoring the active filters) so the tiles always read
    // as fixed reference points -- "418 total, 263 available" -- rather than
    // changing shape every time someone narrows the table below them.
    public class AssetIndexViewModel
    {
        public List<AssetResponseDTO> Assets { get; set; } = new();

        public int TotalCount { get; set; }
        public int AvailableCount { get; set; }
        public int AssignedCount { get; set; }
        public int UnderMaintenanceCount { get; set; }
        public int DisposedCount { get; set; }

        // Echoed back into the filter form so selections persist across
        // paging and across a fresh search.
        public string? Keyword { get; set; }
        public int? CategoryId { get; set; }
        public int? DepartmentId { get; set; }
        public AssetStatus? Status { get; set; }
        public ConditionStatus? Condition { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // Count of rows matching the CURRENT filters, before paging --
        // distinct from TotalCount, which ignores filters entirely.
        public int TotalFilteredCount { get; set; }

        public int TotalPages =>
            TotalFilteredCount == 0 ? 1 : (int)Math.Ceiling(TotalFilteredCount / (double)PageSize);

        public int FirstRowNumber => TotalFilteredCount == 0 ? 0 : (Page - 1) * PageSize + 1;
        public int LastRowNumber => Math.Min(Page * PageSize, TotalFilteredCount);
    }
}