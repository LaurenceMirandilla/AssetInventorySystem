using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    public class UpdateAssetDTO
    {
        [Required, MaxLength(150)]
        public string AssetName { get; set; } = null!;

        [MaxLength(500)] 
        public string? Description { get; set; }

        [MaxLength(100)]
        public string? SerialNumber { get; set; }

        [MaxLength(255)]
        public string? WarrantyInformation { get; set; }

        [MaxLength(500)]
        public string? ImageURL { get; set; }

        public int? CurrentLocationID { get; set; }
        [Required]
        public int? BranchID { get; set; } // NEW


        // Needed for optimistic concurrency check —
        // client must send back the RowVersion it last read
        [Required]
        public byte[] RowVersion { get; set; } = null!;
    }
}