namespace SchoolInventoryManagement.BLL.DTOs
{
    public class ModelDTO
    {
        public int ModelID { get; set; }
        public string ModelName { get; set; } = null!;
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;

        // Same reason as CategoryDTO.Description — the Edit screen needs
        // the current value or it silently blanks the column on save.
        public string? Description { get; set; }
    }
}