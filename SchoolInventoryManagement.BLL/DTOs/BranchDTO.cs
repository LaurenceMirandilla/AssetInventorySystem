namespace SchoolInventoryManagement.BLL.DTOs
{
    public class BranchDTO
    {
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public string? Address { get; set; }
        public string? ContactInfo { get; set; }
    }
}