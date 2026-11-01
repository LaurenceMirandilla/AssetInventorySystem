using System;
using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // The Assets page as Teachers and Staff see it: models only, no units
    // and no counts (see AssetsController.CatalogAsync).
    public class AssetCatalogViewModel
    {
        public List<ModelDTO> Models { get; set; } = new();
        public List<CategoryDTO> CategoryOptions { get; set; } = new();

        public string? Keyword { get; set; }
        public int? CategoryId { get; set; }

        public bool HasFilters => !string.IsNullOrWhiteSpace(Keyword) || CategoryId.HasValue;

        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int[] PageSizeOptions { get; set; } = { 10, 20, 50, 100 };

        // Models matching the search, before paging.
        public int TotalCount { get; set; }

        public int TotalPages =>
            TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

        public int FirstRowNumber => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
        public int LastRowNumber => Math.Min(Page * PageSize, TotalCount);
    }
}
