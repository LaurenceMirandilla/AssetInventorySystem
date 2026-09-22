namespace SchoolInventoryManagement.BLL.DTOs
{
    // Lightweight user reference — used when nesting "who did this" inside
    // other response DTOs (RequestedByUser, ApprovedByUser, etc). Never
    // includes PasswordHash or other sensitive fields.
    public class UserSummaryDTO
    {
        public int UserID { get; set; }
        public string FullName { get; set; } = null!;
        public string Email { get; set; } = null!;
        public string RoleName { get; set; } = null!;
    }
}