using System;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One asset currently out with someone. DaysOut is computed against
    // PhilippineTime.Now at query time, the clock the rest of the project uses.
    public class AssignmentReportRowDTO
    {
        public int AssignmentID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;

        public string AssignedToName { get; set; } = null!;
        public string AssignedToUsername { get; set; } = null!;
        public string AssignedToRole { get; set; } = null!;
        public string? DepartmentName { get; set; }

        public string AssignedByName { get; set; } = null!;
        public DateTime AssignmentDate { get; set; }
        public int DaysOut { get; set; }
        public ConditionStatus ConditionOnAssignment { get; set; }
        public string? Remarks { get; set; }

        // When it should come back (the request's return date; none for a
        // direct assignment or a permanent transfer), and whether it's late.
        public DateTime? DueBack { get; set; }
        public bool IsOverdue { get; set; }
    }
}
