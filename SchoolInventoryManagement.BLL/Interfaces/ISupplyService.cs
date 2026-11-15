using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface ISupplyService
    {
        // The Items > Supplies list. With a branch or a location the
        // quantities count only the rooms that match.
        // scopeBranchId: when set, only supplies kept in that branch, with
        // that branch's quantities, whatever the other filters say.
        Task<List<SupplyListRowDTO>> SearchSuppliesAsync(
            string? keyword, int? categoryId, int? branchId, int? locationId, SupplyStockStatus? status,
            int? scopeBranchId = null);

        // The branch a user is limited to when looking at supplies, or
        // null when they see every branch (Principal, Administrator,
        // Asset Officer).
        Task<int?> GetBranchScopeAsync(int userId);

        // How many supplies the user can see, for the "Supplies - n" tab.
        Task<int> CountSuppliesAsync(int? scopeBranchId = null);

        // Adds a supply with its opening quantity in one storage room.
        // Asset Officer and Administrator only.
        Task<int> CreateSupplyAsync(CreateSupplyDTO dto, int actingUserId);

        // Every supply, for the Add stock form.
        Task<List<SupplyChoiceDTO>> GetSupplyChoicesAsync();

        // Adds each line's quantity to its room and writes a "Received"
        // line in the stock history. All lines are saved together or not
        // at all. Asset Officer and Administrator only. Returns how many
        // units were added.
        Task<int> AddStockAsync(AddStockDTO dto, int actingUserId);

        // ---- Move, count, report (Scripts/018) ----

        // Every room each supply is kept in with something to move.
        Task<List<SupplyStockChoiceDTO>> GetStockChoicesAsync();

        // Takes the quantity out of one location and puts it in another,
        // in any branch. Asset Officer and Administrator only.
        Task MoveStockAsync(MoveStockDTO dto, int actingUserId);

        // The supplies kept in one location, with what the system says.
        Task<List<SupplyCountLineDTO>> GetCountSheetAsync(int locationId);

        // Sets each counted line to the number counted and writes an
        // "Adjusted" history line with the reason. Returns how many lines
        // changed. Asset Officer and Administrator only.
        Task<int> SaveCountAsync(SaveSupplyCountDTO dto, int actingUserId);

        Task<SupplyReportDTO> GetReportAsync(DateTime? fromDate, DateTime? toDate, int? branchId, int? departmentId);

        // Low or out of stock, worst first, for the dashboard.
        Task<List<LowSupplyDTO>> GetLowSuppliesAsync(int take = 6);

        // The supply page. Null when there is no such supply.
        // With a scope, only that branch's stock and history; null when
        // the supply is not kept in that branch at all.
        Task<SupplyDetailsDTO?> GetSupplyDetailsAsync(int supplyId, int historyRows = 50, int? scopeBranchId = null);

        // Name, category, unit, price, description and low-stock level.
        // Never the quantity. Asset Officer and Administrator only.
        Task UpdateSupplyAsync(int supplyId, UpdateSupplyDTO dto, int actingUserId);

        // Only while the supply has no stock history beyond its opening
        // quantity, so a supply added by mistake can be removed but one
        // that has been received, released or counted cannot.
        Task DeleteSupplyAsync(int supplyId, int actingUserId);
    }
}
