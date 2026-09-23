using System;
using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Drives Views/Assets/Index.cshtml. The KPI tiles mirror the table
    // beneath them: every filter, status included, narrows the counts, so
    // the tiles always add up to the rows on screen. Selecting a status
    // therefore zeroes the other four tiles -- which is the honest count,
    // and why clicking the highlighted tile clears the status again rather
    // than leaving you stranded on a row of noughts.
    public class AssetIndexViewModel
    {
        public List<AssetResponseDTO> Assets { get; set; } = new();

        // Breakdown of the filtered set. With a status selected, exactly one
        // of these is non-zero and equals TotalFilteredCount.
        public int AvailableCount { get; set; }
        public int AssignedCount { get; set; }
        public int UnderMaintenanceCount { get; set; }
        public int DisposedCount { get; set; }

        // Every asset on record, filters ignored. Only used to caption the
        // page ("8 of 120") so the filtered count reads as a subset.
        public int GrandTotalCount { get; set; }

        // Echoed back into the filter form so selections persist across
        // paging and across a fresh search.
        public string? Keyword { get; set; }
        public int? CategoryId { get; set; }
        public int? DepartmentId { get; set; }
        public AssetStatus? Status { get; set; }
        public ConditionStatus? Condition { get; set; }
        public int? LocationId { get; set; }
        public int? ModelId { get; set; }
        public int? BranchId { get; set; }

        // The cascading filter lists. The existing DTOs already carry each
        // child's parent (ModelDTO.CategoryID, LocationDTO/DepartmentDTO
        // .BranchID), which is all the page needs to narrow one list when
        // its parent changes.
        public List<CategoryDTO> CategoryOptions { get; set; } = new();
        public List<ModelDTO> ModelOptions { get; set; } = new();
        public List<BranchDTO> BranchOptions { get; set; } = new();
        public List<LocationDTO> LocationOptions { get; set; } = new();
        public List<DepartmentDTO> DepartmentOptions { get; set; } = new();

        // True when anything at all is narrowing the list.
        public bool HasFilters =>
            !string.IsNullOrWhiteSpace(Keyword) ||
            CategoryId.HasValue ||
            DepartmentId.HasValue ||
            Status.HasValue ||
            Condition.HasValue ||
            LocationId.HasValue ||
            ModelId.HasValue ||
            BranchId.HasValue;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;

        // Rows matching the current filters, before paging. Also the value
        // of the leading "All" tile, which is what makes it mirror the table.
        public int TotalFilteredCount { get; set; }

        public int TotalPages =>
            TotalFilteredCount == 0 ? 1 : (int)Math.Ceiling(TotalFilteredCount / (double)PageSize);

        public int FirstRowNumber => TotalFilteredCount == 0 ? 0 : (Page - 1) * PageSize + 1;
        public int LastRowNumber => Math.Min(Page * PageSize, TotalFilteredCount);
    }
}