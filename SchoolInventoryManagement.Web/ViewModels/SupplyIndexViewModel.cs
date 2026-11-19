using System;
using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Items > Supplies: the page of rows, the filters that made it, and the
    // lists the filter menu offers.
    public class SupplyIndexViewModel
    {
        public List<SupplyListRowDTO> Supplies { get; set; } = new();

        public string? Keyword { get; set; }
        public int? CategoryId { get; set; }
        public int? BranchId { get; set; }
        public int? LocationId { get; set; }
        public SupplyStockStatus? Status { get; set; }

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int[] PageSizeOptions { get; set; } = Array.Empty<int>();
        public int TotalFilteredCount { get; set; }
        public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalFilteredCount / (double)PageSize));

        // Every supply, and every asset, for the Assets / Supplies switch.
        public int SupplyTotal { get; set; }
        public int AssetTotal { get; set; }

        // True when the user only sees their own branch's supplies.
        public bool ScopedToBranch { get; set; }

        public int ActiveFilterCount =>
            (CategoryId.HasValue ? 1 : 0) + (BranchId.HasValue && !ScopedToBranch ? 1 : 0) +
            (LocationId.HasValue ? 1 : 0) + (Status.HasValue ? 1 : 0);
        public bool HasFilters => ActiveFilterCount > 0 || !string.IsNullOrWhiteSpace(Keyword);

        public List<SupplyOption> CategoryOptions { get; set; } = new();
        public List<SupplyOption> BranchOptions { get; set; } = new();
        public List<SupplyLocationOption> LocationOptions { get; set; } = new();
    }

    public record SupplyOption(int Id, string Name);
    public record SupplyLocationOption(int Id, string Name, int BranchId, string BranchName);

    // A department other than Property, for "Belongs to" on the supply
    // forms: stock it keeps for itself (not requestable).
    public record SupplyDepartmentOption(int Id, string Name, int BranchId, string BranchName);
}
