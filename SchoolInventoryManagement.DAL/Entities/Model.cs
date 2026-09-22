using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Model
    {
        [Key]
        public int ModelID { get; set; }

        public int CategoryID { get; set; }

        [Required]
        [MaxLength(150)]
        public string ModelName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        // Upward Navigation Property
        [ForeignKey(nameof(CategoryID))]
        public Category Category { get; set; } = null!;

        // Downward Navigation Properties
        public ICollection<Asset> Assets { get; set; } = new List<Asset>();
        public ICollection<AssetRequest> AssetRequests { get; set; } = new List<AssetRequest>();
    }
}