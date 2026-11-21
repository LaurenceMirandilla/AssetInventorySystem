namespace SchoolInventoryManagement.BLL.DTOs
{
    public class LocationDTO
    {
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string LocationCode { get; set; } = null!;
        public string? Description { get; set; }
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;

        // The department the room belongs to (Scripts/029), if any.
        public int? DepartmentID { get; set; }
        public string? DepartmentName { get; set; }
    }
}