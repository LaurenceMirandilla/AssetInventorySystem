using System.Collections.Generic;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // The reporting dashboard. Disposed assets are counted and shown but
    // excluded from TotalValue and from the "in service" figures — a
    // disposed unit is still a record, but it is not stock on hand.
    public class InventorySummaryDTO
    {
        public int TotalAssets { get; set; }
        public int InServiceAssets { get; set; }
        public decimal TotalValue { get; set; }

        public int AvailableCount { get; set; }
        public int AssignedCount { get; set; }
        public int UnderMaintenanceCount { get; set; }
        public int DisposedCount { get; set; }
        public int LostOrDamagedCount { get; set; }

        public int PendingRequestCount { get; set; }
        public int OutstandingAssignmentCount { get; set; }
        public int ActiveUserCount { get; set; }

        public List<CountByLabelDTO> ByStatus { get; set; } = new();
        public List<CountByLabelDTO> ByCondition { get; set; } = new();
        public List<CountByLabelDTO> ByCategory { get; set; } = new();
        public List<CountByLabelDTO> ByBranch { get; set; } = new();
        public List<CountByLabelDTO> ByDepartment { get; set; } = new();
    }
}
