namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CategoryDTO
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;

        // Start of every asset code in the category: CHAIR -> CHAIR-0001.
        public string CodePrefix { get; set; } = null!;

        // Carried so the Edit screen can round-trip it. Without this the
        // controller has nothing to pre-fill the textarea from, and the
        // blank post-back overwrites whatever was stored.
        public string? Description { get; set; }

        // Filled by GetAllCategoriesAsync for the Categories list; zero
        // everywhere else.
        public int ModelCount { get; set; }
        public int AssetCount { get; set; }
        public int AvailableCount { get; set; }
    }
}