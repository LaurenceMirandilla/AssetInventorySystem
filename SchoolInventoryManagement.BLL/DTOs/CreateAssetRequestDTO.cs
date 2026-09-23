using System;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class CreateAssetRequestDTO
    {
        [Required]
        public RequestType RequestType { get; set; }

        // Both types: ModelID required, AssetID must be null -- the unit is
        // staff's choice at approval, never the requester's.
        // Borrow: RequestedLocationID must be null.
        // Transfer: RequestedLocationID required.
        // Enforced both in the service AND at the DB level
        // (CK_AssetRequests_TypeFieldRules).
        public int? ModelID { get; set; }
        public int? AssetID { get; set; }
        public int? RequestedLocationID { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        // Required on every new request; nullable so a missing value reaches
        // the service's own check and its readable message.
        public DateTime? NeededFrom { get; set; }
        public DateTime? ReturnBy { get; set; }
    }
}