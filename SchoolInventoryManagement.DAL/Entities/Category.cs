using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.DAL.Entities
{
    public class Category
    {
        [Key]
        public int CategoryID { get; set; }

        // The UNIQUE constraint for CategoryName will be mapped in ApplicationDbContext
        [Required]
        [MaxLength(100)]
        public string CategoryName { get; set; } = null!;

        // Start of every asset code in this category: "CHAIR" gives
        // CHAIR-0001, CHAIR-0002, ... up to CHAIR-10000. Letters and digits
        // only, stored upper-case, unique across categories (UX index in
        // Scripts/008).
        [Required]
        [MaxLength(10)]
        public string CodePrefix { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        // Downward Navigation Property — Category now only points directly
        // to Model. Assets/AssetRequests are reached via Category -> Model -> Asset.
        public ICollection<Model> Models { get; set; } = new List<Model>();
    }
}