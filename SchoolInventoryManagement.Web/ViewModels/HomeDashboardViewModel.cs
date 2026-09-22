using System.Collections.Generic;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.Web.ViewModels
{
    public class HomeDashboardViewModel
    {
        // False for roles that can't view reports (Teacher, Staff, Department
        // Head). It selects which half of the dashboard is populated and
        // rendered: the inventory-wide one below, or the personal one.
        public bool IsReportsViewer { get; set; }

        public string DisplayName { get; set; } = "";

        public int TotalAssets { get; set; }
        public int CurrentlyIssued { get; set; }
        public int UnderMaintenanceCount { get; set; }
        public decimal ValueInService { get; set; }

        public List<CountByLabelDTO> ByCategory { get; set; } = new();

        public int PendingRequestCount { get; set; }

        // The other half of the dashboard, for everyone IsReportsViewer
        // excludes. Those roles cannot see inventory-wide figures, so their
        // panel reports only what is theirs: what they are holding and what
        // they have asked for.
        public int AssetsInMyCare { get; set; }
        public int MyOpenRequestCount { get; set; }

        // Capped, most-recent-first — the same shape as RecentActivity, so
        // the two halves of the dashboard read alike.
        public List<AssetRequestResponseDTO> MyRecentRequests { get; set; } = new();

        // Capped, most-recent-first. Reuses IReportService.GetAuditTrailAsync
        // rather than a new query.
        public List<AuditReportRowDTO> RecentActivity { get; set; } = new();
    }
}