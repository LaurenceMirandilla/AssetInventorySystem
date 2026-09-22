namespace SchoolInventoryManagement.BLL.DTOs
{
    public class LocationDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string? Description { get; set; }
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
    }
}