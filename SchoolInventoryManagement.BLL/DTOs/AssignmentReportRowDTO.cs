using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One asset currently out with someone. DaysOut is computed against
    // DateTime.Now at query time, matching the local-time convention the
    // rest of the project uses (GETDATE(), DateTime.Now).
    public class AssignmentReportRowDTO
    {
        public int AssignmentID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;

        public string AssignedToName { get; set; } = null!;
        public string AssignedToEmail { get; set; } = null!;
        public string AssignedToRole { get; set; } = null!;
        public string? DepartmentName { get; set; }

        public string AssignedByName { get; set; } = null!;
        public DateTime AssignmentDate { get; set; }
        public int DaysOut { get; set; }
        public ConditionStatus ConditionOnAssignment { get; set; }
        public string? Remarks { get; set; }
    }
}
