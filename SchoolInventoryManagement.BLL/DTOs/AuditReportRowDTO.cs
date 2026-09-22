using System;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class AuditReportRowDTO
    {
        public int LogID { get; set; }
        public DateTime LogDateTime { get; set; }

        public int UserID { get; set; }
        public string UserName { get; set; } = null!;
        public string UserRole { get; set; } = null!;

        public string ActionPerformed { get; set; } = null!;
        public int? TargetAssetID { get; set; }
        public string? TargetAssetCode { get; set; }
        public string? Description { get; set; }
        public string? IPAddress { get; set; }
    }
}
