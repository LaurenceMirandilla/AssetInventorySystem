namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CategoryDTO
    {
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;

        // Carried so the Edit screen can round-trip it. Without this the
        // controller has nothing to pre-fill the textarea from, and the
        // blank post-back overwrites whatever was stored.
        public string? Description { get; set; }
    }
}