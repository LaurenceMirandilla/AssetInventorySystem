using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.Web.ViewModels
{
    // Direct, non-request-driven transfer. Only makes sense for Available
    // units sitting in storage — an assigned asset has no location at all,
    // it is with a person, and AssetMovementService rejects it outright.
    public class TransferAssetViewModel
    {
        public int AssetID { get; set; }

        [Required]
        public int DestinationLocationID { get; set; }

        [Required(ErrorMessage = "Say why the asset is being moved.")]
        [MaxLength(500)]
        public string ReasonForTransfer { get; set; } = null!;

        // Optional: null leaves the asset's recorded condition alone.
        public ConditionStatus? ConditionOnTransfer { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }
}
