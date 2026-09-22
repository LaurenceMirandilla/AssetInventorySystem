using System;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class DisposalRecordResponseDTO
    {
        public int DisposalID { get; set; }
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;

        public DateTime DisposalDate { get; set; }
        public string ReasonForDisposal { get; set; } = null!;
        public string DisposalMethod { get; set; } = null!;
        public UserSummaryDTO ApprovedByUser { get; set; } = null!;
        public string? SupportingDocumentationURL { get; set; }
        public string? Notes { get; set; }

        public DateTime? RestoredDate { get; set; }
        public UserSummaryDTO? RestoredByUser { get; set; }
    }
}