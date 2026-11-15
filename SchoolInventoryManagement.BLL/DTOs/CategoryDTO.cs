using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CategoryDTO
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;

        // Assets or Supplies.
        public CategoryKind Kind { get; set; }

        // Third part of every asset code in the category: CHR -> M-LIB-CHR-0001.
        // Null for a supply category.
        public string? CodePrefix { get; set; }

        // Carried so the Edit screen can round-trip it. Without this the
        // controller has nothing to pre-fill the textarea from, and the
        // blank post-back overwrites whatever was stored.
        public string? Description { get; set; }

        // Filled by GetAllCategoriesAsync for the Categories list; zero
        // everywhere else.
        public int ModelCount { get; set; }
        public int AssetCount { get; set; }
        public int AvailableCount { get; set; }

        // For a supply category: how many supplies are in it.
        public int SupplyCount { get; set; }
    }
}