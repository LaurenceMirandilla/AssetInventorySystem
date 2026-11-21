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
        public string? RequesterRole { get; set; }
        public string DepartmentName { get; set; } = null!;
        public string? BranchName { get; set; }
        // Bought for the Property Department's own stock (a borrow short of stock).
        public bool ForStock { get; set; }
        // The requester's room, where it ends up.
        public string? DeliverTo { get; set; }
        public DateTime RequestedOn { get; set; }
        public DateTime? NeededBy { get; set; }
        public string? Reason { get; set; }
        public CategoryKind ItemKind { get; set; }
        public List<PurchaseOrderFormLineDTO> Lines { get; set; } = new();
        public List<PurchaseOrderFormLineDTO> AllItems { get; set; } = new();
        // The supplier list, to pick from (Scripts/033).
        public List<SupplierDTO> Suppliers { get; set; } = new();
    }

    public class PurchaseOrderFormLineDTO
    {
        public int NewItemRequestItemID { get; set; }
        public string ItemName { get; set; } = null!;
        public string? Description { get; set; }
        public string? Unit { get; set; }
        public int Asked { get; set; }
        public int FromStock => Math.Max(0, Asked - ToBuy);
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
        public string? ShipVia { get; set; }
        public string? Fob { get; set; }
        public string? ShippingTerms { get; set; }
        // Blank = none.
        public decimal? Tax { get; set; }
        public decimal? ShippingCost { get; set; }
        public decimal? OtherCost { get; set; }
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
        public string? RequesterRole { get; set; }
        public string DepartmentName { get; set; } = null!;
        public bool ForStock { get; set; }
        public DateTime RequestedOn { get; set; }
        public string? Reason { get; set; }
        public string? RequesterRoom { get; set; }
        public DateTime? NeededBy { get; set; }

        // Where the supplier delivers: the Property Department of the
        // requisition's branch, at its main room.
        public string BranchName { get; set; } = null!;
        public string? BranchAddress { get; set; }
        public string? BranchContact { get; set; }
        public string? DeliverTo { get; set; }

        public string? ShipVia { get; set; }
        public string? Fob { get; set; }
        public string? ShippingTerms { get; set; }
        public decimal Tax { get; set; }
        public decimal ShippingCost { get; set; }
        public decimal OtherCost { get; set; }

        public List<PurchaseOrderLineDTO> Lines { get; set; } = new();
        public decimal Subtotal => Lines.Sum(l => l.Amount);
        public decimal Total => Subtotal + Tax + ShippingCost + OtherCost;

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

namespace SchoolInventoryManagement.BLL.DTOs
{
    // Suppliers (Scripts/033).
    public class SupplierDTO
    {
        public int SupplierID { get; set; }
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? Contact { get; set; }
        public string? Notes { get; set; }
        public int Orders { get; set; }
        public decimal Spent { get; set; }
        public System.DateTime? LastOrder { get; set; }
    }

    public class SupplierDetailDTO : SupplierDTO
    {
        public System.Collections.Generic.List<SupplierOrderDTO> OrderList { get; set; } = new();
    }

    public class SupplierOrderDTO
    {
        public int PurchaseOrderID { get; set; }
        public string PONumber { get; set; } = null!;
        public System.DateTime OrderDate { get; set; }
        public int NewItemRequestID { get; set; }
        public System.Collections.Generic.List<PurchaseOrderLineDTO> Lines { get; set; } = new();
        public decimal Total => System.Linq.Enumerable.Sum(Lines, l => l.Amount);
    }

    public class SaveSupplierDTO
    {
        public int? SupplierID { get; set; }
        public string Name { get; set; } = "";
        public string? Address { get; set; }
        public string? Contact { get; set; }
        public string? Notes { get; set; }
    }

    // One purchase order on Purchasing's list.
    public class PurchaseOrderListDTO
    {
        public int PurchaseOrderID { get; set; }
        public string PONumber { get; set; } = null!;
        public DateTime OrderDate { get; set; }
        public string SupplierName { get; set; } = null!;
        public int NewItemRequestID { get; set; }
        public NewItemStatus RequestStatus { get; set; }
        public string RequestedByUserName { get; set; } = null!;
        public string DepartmentName { get; set; } = null!;
        public string Items { get; set; } = "";
        public decimal Total { get; set; }
        public int PreparedByUserID { get; set; }
        public string PreparedByName { get; set; } = null!;
        public string BranchName { get; set; } = "";
        public List<PurchaseOrderLineDTO> Lines { get; set; } = new();
        public decimal Extras { get; set; }
    }
}
