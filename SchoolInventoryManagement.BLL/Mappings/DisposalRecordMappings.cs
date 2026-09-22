using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Mappings
{
    public static class DisposalRecordMappings
    {
        // Requires disposal.Asset, disposal.ApprovedByUser(.Role),
        // disposal.RestoredByUser(.Role) to be loaded as applicable
        public static DisposalRecordResponseDTO ToResponseDTO(this DisposalRecord disposal)
        {
            return new DisposalRecordResponseDTO
            {
                DisposalID = disposal.DisposalID,
                AssetID = disposal.AssetID,
                AssetCode = disposal.Asset.AssetCode,
                DisposalDate = disposal.DisposalDate,
                ReasonForDisposal = disposal.ReasonForDisposal,
                DisposalMethod = disposal.DisposalMethod,
                ApprovedByUser = disposal.ApprovedByUser.ToSummaryDTO(),
                SupportingDocumentationURL = disposal.SupportingDocumentationURL,
                Notes = disposal.Notes,
                RestoredDate = disposal.RestoredDate,
                RestoredByUser = disposal.RestoredByUser?.ToSummaryDTO()
            };
        }
    }
}