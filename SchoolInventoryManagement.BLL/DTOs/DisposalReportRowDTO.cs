using System;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // One disposal event. An asset can appear more than once over its
    // lifetime — DisposalRecords is one-to-many, and a mistaken disposal
    // that was later restored is still a row worth reporting.
    public class DisposalReportRowDTO
    {
        public int DisposalID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string AssetName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;

        public DateTime DisposalDate { get; set; }
        public string ReasonForDisposal { get; set; } = null!;
        public string DisposalMethod { get; set; } = null!;
        public string ApprovedByName { get; set; } = null!;
        public decimal? AcquisitionCost { get; set; }

        public bool IsRestored { get; set; }
        public DateTime? RestoredDate { get; set; }
        public string? RestoredByName { get; set; }
    }
}
