using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using SchoolInventoryManagement.DAL.Entities.Enums;

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

        // Assets or Supplies (Scripts/015). Existing rows are Asset.
        public CategoryKind Kind { get; set; } = CategoryKind.Asset;

        // Third part of every asset code in this category: "CHR" gives
        // M-LIB-CHR-0001. Exactly three letters, stored upper-case, unique
        // across categories (UX_Categories_CodePrefix). Null for a supply
        // category, which has no codes.
        [MaxLength(3)]
        public string? CodePrefix { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        // Downward Navigation Property — Category now only points directly
        // to Model. Assets/AssetRequests are reached via Category -> Model -> Asset.
        public ICollection<Model> Models { get; set; } = new List<Model>();
    }
}