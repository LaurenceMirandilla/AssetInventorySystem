using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateAssetRequestDTO
    {
        [Required]
        public RequestType RequestType { get; set; }

        // Borrow: ModelID required, AssetID/RequestedLocationID must be null.
        // Transfer: AssetID + RequestedLocationID required, ModelID must be null.
        // Enforced both in the service AND at the DB level
        // (CK_AssetRequests_TypeFieldRules).
        public int? ModelID { get; set; }
        public int? AssetID { get; set; }
        public int? RequestedLocationID { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }
    }
}