using System.Collections.Generic;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class RequestActivityDTO
    {
        public List<RequestActivityRowDTO> Rows { get; set; } = new();

        public List<CountByLabelDTO> ByStatus { get; set; } = new();
        public List<CountByLabelDTO> ByType { get; set; } = new();
        public List<CountByLabelDTO> ByDepartment { get; set; } = new();

        public int TotalRequests { get; set; }
        public int DecidedRequests { get; set; }

        // Mean days from submission to decision across DecidedRequests.
        // Null when nothing in range has been decided yet.
        public double? AverageTurnaroundDays { get; set; }
    }
}
