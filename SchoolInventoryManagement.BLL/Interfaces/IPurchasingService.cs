using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Purchase orders for what requisitions are missing (Scripts/031).
    public interface IPurchasingService
    {
        // The requisition's lines still to put on an order.
        Task<PurchaseOrderFormDTO> GetOrderFormAsync(int requestId, int actingUserId);

        // Saves the order (PO-yyyy-nnnn). When nothing is left to order, the
        // requisition moves on to waiting for the supplier.
        Task<PurchaseOrderDTO> CreateOrderAsync(CreatePurchaseOrderDTO dto, int actingUserId);

        Task<PurchaseOrderDTO> GetOrderAsync(int purchaseOrderId, int actingUserId);

        // Until the items are received from the supplier. Returns the
        // requisition it was for.
        Task<int> DeleteOrderAsync(int purchaseOrderId, int actingUserId);

        // Suppliers (Scripts/033): the list with how much was ordered from
        // each, one supplier with its orders and prices, and adding or
        // editing one. Returns the supplier's id.
        Task<List<SupplierDTO>> GetSuppliersAsync(int actingUserId);
        Task<SupplierDetailDTO> GetSupplierAsync(int supplierId, int actingUserId);
        Task<int> SaveSupplierAsync(SaveSupplierDTO dto, int actingUserId);
    }
}
