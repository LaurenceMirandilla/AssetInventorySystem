namespace SchoolInventoryManagement.BLL.DTOs
{
    // One row of any "group and count" breakdown on a report — by status,
    // by category, by branch, and so on. TotalValue sums AcquisitionCost
    // for the group, treating a null cost as zero rather than excluding
    // the asset, so Count and TotalValue always describe the same set.
    public class CountByLabelDTO
    {
        public string Label { get; set; } = null!;
        public int Count { get; set; }
        public decimal TotalValue { get; set; }
    }
}
