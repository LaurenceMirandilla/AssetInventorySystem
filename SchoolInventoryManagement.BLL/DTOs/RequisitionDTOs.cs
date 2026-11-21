using System;
using System.Collections.Generic;
using System.Linq;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.DTOs
{
    // Pick from stock (Scripts/031): the fixed-asset lines of a requisition
    // that Property's stock covers, each with the units it can send.
    public class PickUnitsDTO
    {
        public int NewItemRequestID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public string BranchName { get; set; } = null!;
        public NewItemStatus RequestStatus { get; set; }
        public List<PickLineDTO> Lines { get; set; } = new();
    }

    public class PickLineDTO
    {
        public int NewItemRequestItemID { get; set; }
        public string ItemName { get; set; } = null!;
        public string? Description { get; set; }
        // How many of the line come from stock.
        public int FromStock { get; set; }
        public List<PickUnitChoiceDTO> Units { get; set; } = new();
    }

    public class PickUnitChoiceDTO
    {
        public int AssetID { get; set; }
        public string AssetCode { get; set; } = null!;
        public string? SerialNumber { get; set; }
        public string? LocationName { get; set; }
        public string Condition { get; set; } = null!;
        public bool Picked { get; set; }
    }

    // A purchase order to make (Scripts/031): the requisition's lines still
    // to buy.
    public class PurchaseOrderFormDTO
    {
        public int NewItemRequestID { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public DateTime? NeededBy { get; set; }
        public string? Reason { get; set; }
        public CategoryKind ItemKind { get; set; }
        public List<PurchaseOrderFormLineDTO> Lines { get; set; } = new();
        // Earlier orders' suppliers, to pick from.
        public List<string> KnownSuppliers { get; set; } = new();
    }

    public class PurchaseOrderFormLineDTO
    {
        public int NewItemRequestItemID { get; set; }
        public string ItemName { get; set; } = null!;
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public int ToBuy { get; set; }
        public int Ordered { get; set; }
        public int Left => Math.Max(0, ToBuy - Ordered);
    }

    public class CreatePurchaseOrderDTO
    {
        public int NewItemRequestID { get; set; }
        public string SupplierName { get; set; } = "";
        public string? SupplierAddress { get; set; }
        public string? SupplierContact { get; set; }
        public DateTime OrderDate { get; set; }
        public string? Notes { get; set; }
        public List<CreatePurchaseOrderLineDTO> Lines { get; set; } = new();
    }

    public class CreatePurchaseOrderLineDTO
    {
        public int NewItemRequestItemID { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    // A purchase order to read or print.
    public class PurchaseOrderDTO
    {
        public int PurchaseOrderID { get; set; }
        public string PONumber { get; set; } = null!;
        public DateTime OrderDate { get; set; }
        public string SupplierName { get; set; } = null!;
        public string? SupplierAddress { get; set; }
        public string? SupplierContact { get; set; }
        public string? Notes { get; set; }
        public string PreparedByName { get; set; } = null!;
        public string? PreparedByRole { get; set; }
        public DateTime CreatedAt { get; set; }

        public int NewItemRequestID { get; set; }
        public NewItemStatus RequestStatus { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public DateTime? NeededBy { get; set; }

        // Where the supplier delivers: the Property Department of the
        // requisition's branch, at its main room.
        public string BranchName { get; set; } = null!;
        public string? DeliverTo { get; set; }

        public List<PurchaseOrderLineDTO> Lines { get; set; } = new();
        public decimal Total => Lines.Sum(l => l.Amount);

        // Can still be deleted: not received from the supplier yet.
        public bool CanDelete { get; set; }
    }

    public class PurchaseOrderLineDTO
    {
        public string ItemName { get; set; } = null!;
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount => Quantity * UnitPrice;
    }
}
