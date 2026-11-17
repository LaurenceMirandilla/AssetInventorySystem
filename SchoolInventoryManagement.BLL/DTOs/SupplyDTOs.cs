using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // In stock, running low, or none left. Worked out from the total and
    // the supply's low-stock level; never stored.
    public enum SupplyStockStatus
    {
        InStock,
        RunningLow,
        OutOfStock
    }

    // One row of the Items > Supplies list.
    public class SupplyListRowDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal? UnitPrice { get; set; }
        public int LowStockLevel { get; set; }
        public string? Description { get; set; }

        // Every room added together (or only the rooms the list was
        // filtered to).
        public int TotalQuantity { get; set; }

        // The rooms it is kept in, by name.
        public List<string> LocationNames { get; set; } = new();

        // The smallest amount any one branch that keeps it has. The
        // low-stock level is checked against each branch, not the total.
        public int LowestBranchQuantity { get; set; }
        // A branch that keeps it is at or under its own low-stock level.
        public bool AnyBranchLow { get; set; }

        public SupplyStockStatus Status =>
            TotalQuantity <= 0 ? SupplyStockStatus.OutOfStock :
            AnyBranchLow ? SupplyStockStatus.RunningLow :
            SupplyStockStatus.InStock;
    }

    // The supply page: its facts, where it is kept, and its stock history.
    public class SupplyDetailsDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public int CategoryID { get; set; }
        public string CategoryName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public decimal? UnitPrice { get; set; }
        public int LowStockLevel { get; set; }
        public string? Description { get; set; }

        public int TotalQuantity { get; set; }
        public List<SupplyStockRowDTO> Stock { get; set; } = new();

        // The smallest amount any one branch that keeps it has. The
        // low-stock level is checked against each branch, not the total.
        public int LowestBranchQuantity { get; set; }
        // A branch that keeps it is at or under its own low-stock level.
        public bool AnyBranchLow { get; set; }

        // Each branch's total, and whether it is at or under the level.
        public List<SupplyBranchStockDTO> Branches { get; set; } = new();

        // Every branch, with its own level if it has one. For the edit form.
        public List<SupplyBranchLevelDTO> BranchLevels { get; set; } = new();

        // How many purchases the average price is worked out from.
        public int PricedPurchases { get; set; }

        // Newest first; HistoryCount is how many there are in all.
        public List<SupplyHistoryRowDTO> History { get; set; } = new();
        public int HistoryCount { get; set; }

        // Why Delete is off, or null when the supply can be deleted.
        public string? DeleteBlocked { get; set; }

        public SupplyStockStatus Status =>
            TotalQuantity <= 0 ? SupplyStockStatus.OutOfStock :
            AnyBranchLow ? SupplyStockStatus.RunningLow :
            SupplyStockStatus.InStock;
    }

    public class SupplyBranchStockDTO
    {
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public int Quantity { get; set; }
        // The level this branch is checked against: its own, or the
        // supply's general one.
        public int LowStockLevel { get; set; }
        public bool HasOwnLevel { get; set; }
        public bool IsLow { get; set; }
    }

    // One branch's low-stock level on the edit form. Empty = use the
    // general level.
    public class SupplyBranchLevelDTO
    {
        public int BranchID { get; set; }
        public string BranchName { get; set; } = "";
        [Range(0, 1000000, ErrorMessage = "Enter a level of 0 or more.")]
        public int? LowStockLevel { get; set; }
        // What that branch holds now, for the form.
        public int Quantity { get; set; }
    }

    public class SupplyStockRowDTO
    {
        public string LocationName { get; set; } = null!;
        public int BranchID { get; set; }
        public string BranchName { get; set; } = null!;
        public int Quantity { get; set; }
    }

    public class SupplyHistoryRowDTO
    {
        public System.DateTime CreatedAt { get; set; }
        // Opening, Received, Issued or Adjusted.
        public string Type { get; set; } = null!;
        public string LocationName { get; set; } = null!;
        public string? Note { get; set; }
        public string PerformedBy { get; set; } = null!;
        public int QuantityChange { get; set; }
        public int QuantityAfter { get; set; }
        // On stock coming in: what each unit cost.
        public decimal? UnitPrice { get; set; }
    }

    // What can be changed after a supply exists. Quantities are not here:
    // stock only changes through receiving, releasing and counting.
    public class UpdateSupplyDTO
    {
        [Required(ErrorMessage = "Enter a name.")]
        [MaxLength(150)]
        [Display(Name = "Name")]
        public string SupplyName { get; set; } = null!;

        [Required(ErrorMessage = "Pick a category.")]
        [Display(Name = "Category")]
        public int? CategoryID { get; set; }

        [Required(ErrorMessage = "Enter what it is counted in, e.g. box.")]
        [MaxLength(30)]
        [Display(Name = "Counted per")]
        public string Unit { get; set; } = null!;

        [MaxLength(255)]
        public string? Description { get; set; }

        [Range(0, 1000000, ErrorMessage = "Enter a level of 0 or more.")]
        [Display(Name = "Low-stock level")]
        public int LowStockLevel { get; set; }

        // Branches with a level of their own. A branch left empty uses
        // the general level above.
        public List<SupplyBranchLevelDTO> BranchLevels { get; set; } = new();
    }

    // "Add stock": supplies that arrived without a new-item request
    // (a direct purchase, unused supplies handed back).
    public class AddStockDTO
    {
        [Required(ErrorMessage = "Pick where the stock came from.")]
        [MaxLength(40)]
        public string Source { get; set; } = null!;

        // Who sold or returned it. Optional.
        [MaxLength(100)]
        [Display(Name = "From")]
        public string? SourceName { get; set; }

        [MaxLength(50)]
        [Display(Name = "Receipt or reference no.")]
        public string? Reference { get; set; }

        public List<AddStockLineDTO> Lines { get; set; } = new();
    }

    public class AddStockLineDTO
    {
        public int? SupplyID { get; set; }
        public int Quantity { get; set; }
        public int? LocationID { get; set; }

        // What each unit cost this time. Optional; when given, the
        // supply's average price is worked out again.
        [Range(0, 9999999.99, ErrorMessage = "Enter a price of 0 or more.")]
        public decimal? UnitPrice { get; set; }
    }

    // A supply as the Add stock form lists it.
    public class SupplyChoiceDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int TotalQuantity { get; set; }
        // The first room it is already kept in, offered as the default.
        public int? DefaultLocationID { get; set; }
    }

    public class CreateSupplyDTO
    {
        [Required(ErrorMessage = "Enter a name.")]
        [MaxLength(150)]
        [Display(Name = "Name")]
        public string SupplyName { get; set; } = null!;

        [Required(ErrorMessage = "Pick a category.")]
        [Display(Name = "Category")]
        public int? CategoryID { get; set; }

        [Required(ErrorMessage = "Enter what it is counted in, e.g. box.")]
        [MaxLength(30)]
        [Display(Name = "Counted per")]
        public string Unit { get; set; } = null!;

        [Range(0, 9999999.99, ErrorMessage = "Enter a price of 0 or more.")]
        [Display(Name = "Price per unit")]
        public decimal? UnitPrice { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        [Range(0, 1000000, ErrorMessage = "Enter a quantity of 0 or more.")]
        [Display(Name = "Opening quantity")]
        public int OpeningQuantity { get; set; }

        [Required(ErrorMessage = "Pick where it is kept.")]
        [Display(Name = "Kept in")]
        public int? LocationID { get; set; }

        [Range(0, 1000000, ErrorMessage = "Enter a level of 0 or more.")]
        [Display(Name = "Low-stock level")]
        public int LowStockLevel { get; set; }
    }
}
