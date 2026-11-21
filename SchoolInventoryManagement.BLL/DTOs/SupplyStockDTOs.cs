using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // ---- Move stock between locations ----
    public class MoveStockDTO
    {
        [Required(ErrorMessage = "Pick a supply.")]
        public int? SupplyID { get; set; }

        [Required(ErrorMessage = "Pick where it is moved from.")]
        public int? FromLocationID { get; set; }

        [Required(ErrorMessage = "Pick where it is moved to.")]
        public int? ToLocationID { get; set; }

        [Range(1, 1000000, ErrorMessage = "Enter a quantity of 1 or more.")]
        public int Quantity { get; set; } = 1;

        [MaxLength(150)]
        public string? Note { get; set; }
    }

    // Where each supply is kept, for the Move stock form.
    public class SupplyStockChoiceDTO
    {
        public int SupplyID { get; set; }
        public int LocationID { get; set; }
        public string LocationName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public int Quantity { get; set; }
        public string DepartmentName { get; set; } = null!;
        public bool IsProperty { get; set; }

        // "Main Storage Room (Annex Campus)", plus whose it is when it is
        // not the Property Department's.
        public string Label => $"{LocationName} ({BranchName})" + (IsProperty ? "" : $" · {DepartmentName}'s");
    }

    // ---- Count supplies ----
    public class SupplyCountLineDTO
    {
        public string DepartmentName { get; set; } = null!;
        public bool IsProperty { get; set; }
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string CategoryName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int SystemQuantity { get; set; }
    }

    public class SaveSupplyCountDTO
    {
        public int LocationID { get; set; }
        public List<SaveSupplyCountLineDTO> Lines { get; set; } = new();
    }

    public class SaveSupplyCountLineDTO
    {
        public int SupplyID { get; set; }
        // What the page showed when it was opened.
        public int SystemQuantity { get; set; }
        // Blank means "not counted": the line is left alone.
        public int? Counted { get; set; }
        [MaxLength(150)]
        public string? Reason { get; set; }
    }

    // ---- Report ----
    public class SupplyReportDTO
    {
        public int RequestsReleased { get; set; }
        public int UnitsIssued { get; set; }
        public decimal ValueIssued { get; set; }
        public int RunningLow { get; set; }
        public int OutOfStock { get; set; }
        public List<SupplyReportDepartmentDTO> ByDepartment { get; set; } = new();
        public List<SupplyReportItemDTO> ByItem { get; set; } = new();

        // Who the supplies went to, person by person, under the department
        // each request was made for.
        public List<SupplyReportPersonDTO> ByPerson { get; set; } = new();

        // Requested vs handed out: every supply request made in the period,
        // whatever became of it.
        public SupplyDemandDTO DemandTotal { get; set; } = new();
        public List<SupplyDemandDTO> DemandByDepartment { get; set; } = new();
        // The supplies people asked for but did not get (lowered or
        // rejected), most first.
        public List<SupplyUnmetDTO> MostUnmet { get; set; } = new();
    }

    public class SupplyReportPersonDTO
    {
        public int UserID { get; set; }
        public string PersonName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public int Requests { get; set; }
        public int Units { get; set; }
        public decimal Value { get; set; }
    }

    // Units asked for on supply requests, and what became of them.
    public class SupplyDemandDTO
    {
        public string DepartmentName { get; set; } = "";
        public int Requests { get; set; }
        public int Asked { get; set; }
        // Released or delivered.
        public int HandedOut { get; set; }
        // Approved for less than asked.
        public int Lowered { get; set; }
        public int Rejected { get; set; }
        public int Cancelled { get; set; }
        // Waiting for approval, or approved and not released yet.
        public int StillOpen { get; set; }
        // Handed out, of what was asked on requests that are settled.
        public int? FilledPercent => Asked - StillOpen <= 0 ? null
            : (int)Math.Round(100.0 * HandedOut / (Asked - StillOpen));
    }

    public class SupplyUnmetDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int Asked { get; set; }
        public int NotGiven { get; set; }
    }

    public class SupplyReportDepartmentDTO
    {
        public string DepartmentName { get; set; } = null!;
        public int Requests { get; set; }
        public int Units { get; set; }
        public decimal Value { get; set; }
    }

    public class SupplyReportItemDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int Received { get; set; }
        public int Issued { get; set; }
        public int Adjusted { get; set; }
        public int InStock { get; set; }
        // Taken out as unusable (damaged, expired, lost).
        public int Removed { get; set; }
        // The smallest amount any one branch that keeps it has.
        public int LowestBranchQuantity { get; set; }
        // A branch that keeps it is at or under its own low-stock level.
        public bool AnyBranchLow { get; set; }
        public int LowStockLevel { get; set; }
        public SupplyStockStatus Status =>
            InStock <= 0 ? SupplyStockStatus.OutOfStock :
            AnyBranchLow ? SupplyStockStatus.RunningLow :
            SupplyStockStatus.InStock;
    }

    // A supply that is low or out, for the dashboard card.
    public class LowSupplyDTO
    {
        public int SupplyID { get; set; }
        public string SupplyName { get; set; } = null!;
        public string Unit { get; set; } = null!;
        public int Quantity { get; set; }
        public int LowStockLevel { get; set; }
        // The branch that is low or out.
        public string BranchName { get; set; } = "";
    }

    // Taking stock out because it cannot be used.
    public class RemoveStockDTO
    {
        [Required(ErrorMessage = "Pick a supply.")]
        public int? SupplyID { get; set; }

        [Required(ErrorMessage = "Pick where it is taken from.")]
        public int? LocationID { get; set; }

        [Range(1, 1000000, ErrorMessage = "Enter a quantity of 1 or more.")]
        public int Quantity { get; set; } = 1;

        [Required(ErrorMessage = "Pick a reason.")]
        [MaxLength(40)]
        public string Reason { get; set; } = null!;

        [MaxLength(150)]
        public string? Note { get; set; }
    }
}
