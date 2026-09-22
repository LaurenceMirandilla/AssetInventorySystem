using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    // Read-only aggregation over the existing tables. Nothing here writes,
    // so unlike every other service in this project there is no RowVersion
    // handling, no ConcurrencyConflictException, and nothing for the audit
    // interceptor to pick up.
    //
    // Every method still takes actingUserId and re-checks permission
    // independently of the controller's [Authorize], per the defence-in-depth
    // pattern used throughout the BLL.
    //
    // Date ranges are inclusive at both ends: a ToDate of 5 March includes
    // everything that happened on 5 March. Callers pass plain dates; the
    // service handles the end-of-day boundary.
    public interface IReportService
    {
        Task<InventorySummaryDTO> GetInventorySummaryAsync(int actingUserId);

        Task<List<AssignmentReportRowDTO>> GetOutstandingAssignmentsAsync(
            int? departmentId, int? branchId, int actingUserId);

        Task<List<DisposalReportRowDTO>> GetDisposalsAsync(
            DateTime? fromDate, DateTime? toDate, bool includeRestored, int actingUserId);

        Task<RequestActivityDTO> GetRequestActivityAsync(
            DateTime? fromDate, DateTime? toDate,
            RequestStatus? status, RequestType? type, int actingUserId);

        Task<List<AuditReportRowDTO>> GetAuditTrailAsync(
            DateTime? fromDate, DateTime? toDate, int? userId,
            string? actionContains, int maxRows, int actingUserId);
    }
}
