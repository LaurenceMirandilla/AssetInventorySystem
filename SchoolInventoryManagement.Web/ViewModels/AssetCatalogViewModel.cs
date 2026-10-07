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
    }
}
