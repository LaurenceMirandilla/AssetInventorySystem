using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public class ReportService : IReportService
    {
        private readonly ApplicationDbContext _context;

        public ReportService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==================================================================
        // Inventory summary
        // ==================================================================

        public async Task<InventorySummaryDTO> GetInventorySummaryAsync(int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            // "In service" excludes Disposed. A disposed unit keeps its row
            // for history but is not stock, so counting it in totals or in
            // value would overstate what the school actually holds.
            var assets = _context.Assets;
            var inService = assets.Where(a => a.Status != AssetStatus.Disposed);

            var summary = new InventorySummaryDTO
            {
                TotalAssets = await assets.CountAsync(),
                InServiceAssets = await inService.CountAsync(),

                // AcquisitionCost is nullable — treat an unpriced asset as
                // zero rather than dropping it, so Count and TotalValue
                // always describe the same set of rows.
                TotalValue = await inService.SumAsync(a => a.AcquisitionCost ?? 0m),

                AvailableCount = await assets.CountAsync(a => a.Status == AssetStatus.Available),
                AssignedCount = await assets.CountAsync(a => a.Status == AssetStatus.Assigned),
                UnderMaintenanceCount = await assets.CountAsync(a => a.Status == AssetStatus.UnderMaintenance),
                DisposedCount = await assets.CountAsync(a => a.Status == AssetStatus.Disposed),
                LostOrDamagedCount = await assets.CountAsync(a =>
                    a.Status == AssetStatus.Lost || a.Status == AssetStatus.Damaged),

                PendingRequestCount = await _context.AssetRequests
                    .CountAsync(r => r.RequestStatus == RequestStatus.Pending),

                OutstandingAssignmentCount = await _context.AssetAssignments
                    .CountAsync(a => a.ReturnDate == null),

                ActiveUserCount = await _context.Users.CountAsync(u => u.Status == "Active")
            };

            // Every breakdown below covers ALL assets including disposed, so
            // the Disposed slice is visible rather than silently missing.
            summary.ByStatus = await assets
                .GroupBy(a => a.Status)
                .Select(g => new CountByLabelDTO
                {
                    Label = g.Key.ToString(),
                    Count = g.Count(),
                    TotalValue = g.Sum(a => a.AcquisitionCost ?? 0m)
                })
                .ToListAsync();

            summary.ByCondition = await assets
                .GroupBy(a => a.Condition)
                .Select(g => new CountByLabelDTO
                {
                    Label = g.Key.ToString(),
                    Count = g.Count(),
                    TotalValue = g.Sum(a => a.AcquisitionCost ?? 0m)
                })
                .ToListAsync();

            summary.ByCategory = await assets
                .GroupBy(a => a.Model.Category.CategoryName)
                .Select(g => new CountByLabelDTO
                {
                    Label = g.Key,
                    Count = g.Count(),
                    TotalValue = g.Sum(a => a.AcquisitionCost ?? 0m)
                })
                .ToListAsync();

            summary.ByBranch = await assets
                .GroupBy(a => a.Branch.BranchName)
                .Select(g => new CountByLabelDTO
                {
                    Label = g.Key,
                    Count = g.Count(),
                    TotalValue = g.Sum(a => a.AcquisitionCost ?? 0m)
                })
                .ToListAsync();

            // Asset.DepartmentID is nullable, and an assigned asset can sit
            // with a person outside any department — group those under a
            // label rather than dropping them from the report.
            summary.ByDepartment = await assets
                .GroupBy(a => a.Department != null ? a.Department.DepartmentName : "(unassigned)")
                .Select(g => new CountByLabelDTO
                {
                    Label = g.Key,
                    Count = g.Count(),
                    TotalValue = g.Sum(a => a.AcquisitionCost ?? 0m)
                })
                .ToListAsync();

            // Ordering in memory: SQL Server would order these by the raw
            // string, and "biggest group first" is what a reader wants.
            summary.ByStatus = summary.ByStatus.OrderByDescending(x => x.Count).ToList();
            summary.ByCondition = summary.ByCondition.OrderByDescending(x => x.Count).ToList();
            summary.ByCategory = summary.ByCategory.OrderByDescending(x => x.Count).ToList();
            summary.ByBranch = summary.ByBranch.OrderByDescending(x => x.Count).ToList();
            summary.ByDepartment = summary.ByDepartment.OrderByDescending(x => x.Count).ToList();

            return summary;
        }

        // ==================================================================
        // Outstanding assignments — what is out with whom, right now
        // ==================================================================

        public async Task<List<AssignmentReportRowDTO>> GetOutstandingAssignmentsAsync(
            int? departmentId, int? branchId, int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            var query = _context.AssetAssignments
                .Include(a => a.Asset)
                    .ThenInclude(x => x.Department)
                .Include(a => a.Asset)
                    .ThenInclude(x => x.Branch)
                .Include(a => a.AssignedToUser)
                    .ThenInclude(u => u.Role)
                .Include(a => a.AssignedByUser)
                .Where(a => a.ReturnDate == null);

            if (departmentId.HasValue)
                query = query.Where(a => a.Asset.DepartmentID == departmentId.Value);

            if (branchId.HasValue)
                query = query.Where(a => a.Asset.BranchID == branchId.Value);

            var assignments = await query
                .OrderBy(a => a.AssignmentDate)
                .ToListAsync();

            var now = DateTime.Now;

            return assignments.Select(a => new AssignmentReportRowDTO
            {
                AssignmentID = a.AssignmentID,
                AssetID = a.AssetID,
                AssetCode = a.Asset.AssetCode,
                AssetName = a.Asset.AssetName,
                AssignedToName = $"{a.AssignedToUser.FirstName} {a.AssignedToUser.LastName}",
                AssignedToEmail = a.AssignedToUser.Email,
                AssignedToRole = a.AssignedToUser.Role.RoleName,
                DepartmentName = a.Asset.Department?.DepartmentName,
                AssignedByName = $"{a.AssignedByUser.FirstName} {a.AssignedByUser.LastName}",
                AssignmentDate = a.AssignmentDate,
                DaysOut = Math.Max(0, (int)(now.Date - a.AssignmentDate.Date).TotalDays),
                ConditionOnAssignment = a.ConditionOnAssignment,
                Remarks = a.Remarks
            }).ToList();
        }

        // ==================================================================
        // Disposals
        // ==================================================================

        public async Task<List<DisposalReportRowDTO>> GetDisposalsAsync(
            DateTime? fromDate, DateTime? toDate, bool includeRestored, int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            var query = _context.DisposalRecords
                .Include(d => d.Asset)
                    .ThenInclude(a => a.Model)
                        .ThenInclude(m => m.Category)
                .Include(d => d.ApprovedByUser)
                .Include(d => d.RestoredByUser)
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(d => d.DisposalDate >= fromDate.Value.Date);

            // Inclusive upper bound: everything up to the last tick of ToDate.
            if (toDate.HasValue)
                query = query.Where(d => d.DisposalDate < toDate.Value.Date.AddDays(1));

            // "Active disposal" is the row where RestoredDate IS NULL — a
            // restored one records a disposal that was undone.
            if (!includeRestored)
                query = query.Where(d => d.RestoredDate == null);

            var disposals = await query
                .OrderByDescending(d => d.DisposalDate)
                .ToListAsync();

            return disposals.Select(d => new DisposalReportRowDTO
            {
                DisposalID = d.DisposalID,
                AssetID = d.AssetID,
                AssetCode = d.Asset.AssetCode,
                AssetName = d.Asset.AssetName,
                CategoryName = d.Asset.Model.Category.CategoryName,
                DisposalDate = d.DisposalDate,
                ReasonForDisposal = d.ReasonForDisposal,
                DisposalMethod = d.DisposalMethod,
                ApprovedByName = $"{d.ApprovedByUser.FirstName} {d.ApprovedByUser.LastName}",
                AcquisitionCost = d.Asset.AcquisitionCost,
                IsRestored = d.RestoredDate != null,
                RestoredDate = d.RestoredDate,
                RestoredByName = d.RestoredByUser == null
                    ? null
                    : $"{d.RestoredByUser.FirstName} {d.RestoredByUser.LastName}"
            }).ToList();
        }

        // ==================================================================
        // Request activity
        // ==================================================================

        public async Task<RequestActivityDTO> GetRequestActivityAsync(
            DateTime? fromDate, DateTime? toDate,
            RequestStatus? status, RequestType? type, int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            var query = _context.AssetRequests
                .Include(r => r.RequestedByUser)
                    .ThenInclude(u => u.Role)
                .Include(r => r.Department)
                .Include(r => r.Items)
                    .ThenInclude(i => i.Model)
                .Include(r => r.Model)
                .Include(r => r.Asset)
                .Include(r => r.RequestedLocation)
                .Include(r => r.ApprovedByUser)
                    .ThenInclude(u => u!.Role)
                .AsSplitQuery()
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(r => r.RequestDate >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(r => r.RequestDate < toDate.Value.Date.AddDays(1));

            if (status.HasValue)
                query = query.Where(r => r.RequestStatus == status.Value);

            if (type.HasValue)
                query = query.Where(r => r.RequestType == type.Value);

            var requests = await query
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            var rows = requests.Select(r => new RequestActivityRowDTO
            {
                RequestID = r.RequestID,
                RequestType = r.RequestType,
                RequestStatus = r.RequestStatus,
                RequesterName = $"{r.RequestedByUser.FirstName} {r.RequestedByUser.LastName}",
                RequesterRole = r.RequestedByUser.Role.RoleName,
                DepartmentName = r.Department.DepartmentName,
                ItemDescription = DescribeRequestedItem(r),
                Reason = r.Reason,
                RequestDate = r.RequestDate,
                ApprovalDate = r.ApprovalDate,
                DecidedByName = r.ApprovedByUser == null
                    ? null
                    : $"{r.ApprovedByUser.FirstName} {r.ApprovedByUser.LastName}",
                DecidedByRole = r.ApprovedByUser?.Role.RoleName,
                TurnaroundDays = r.ApprovalDate.HasValue
                    ? Math.Round((r.ApprovalDate.Value - r.RequestDate).TotalDays, 1)
                    : (double?)null,
                Remarks = r.Remarks
            }).ToList();

            var decided = rows.Where(x => x.TurnaroundDays.HasValue).ToList();

            return new RequestActivityDTO
            {
                Rows = rows,
                TotalRequests = rows.Count,
                DecidedRequests = decided.Count,
                AverageTurnaroundDays = decided.Count == 0
                    ? null
                    : Math.Round(decided.Average(x => x.TurnaroundDays!.Value), 1),

                ByStatus = rows
                    .GroupBy(x => x.RequestStatus.ToString())
                    .Select(g => new CountByLabelDTO { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList(),

                ByType = rows
                    .GroupBy(x => x.RequestType.ToString())
                    .Select(g => new CountByLabelDTO { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList(),

                ByDepartment = rows
                    .GroupBy(x => x.DepartmentName)
                    .Select(g => new CountByLabelDTO { Label = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .ToList()
            };
        }

        // The ticket's lines ("Chair x10, Stand Fan x2"), plus the
        // destination for a Transfer. One string keeps the report to one
        // column. A row from before tickets had lines falls back to the
        // model or unit it named.
        private static string DescribeRequestedItem(AssetRequest request)
        {
            var what = request.Items.Count > 0
                ? string.Join(", ", request.Items
                    .OrderBy(i => i.Model?.ModelName)
                    .Select(i => i.Quantity > 1
                        ? $"{i.Model?.ModelName ?? "(model missing)"} x{i.Quantity}"
                        : i.Model?.ModelName ?? "(model missing)"))
                : request.Model?.ModelName ?? request.Asset?.AssetCode ?? "(item missing)";

            if (request.RequestType == RequestType.Borrow)
                return what;

            var destination = request.RequestedLocation?.LocationName ?? "(location missing)";
            return $"{what} -> {destination}";
        }

        // ==================================================================
        // Audit trail
        // ==================================================================

        public async Task<List<AuditReportRowDTO>> GetAuditTrailAsync(
            DateTime? fromDate, DateTime? toDate, int? userId,
            string? actionContains, int maxRows, int actingUserId)
        {
            await PermissionHelper.EnsureCanViewReportsAsync(_context, actingUserId);

            // AuditLogs grows without bound and nothing prunes it, so this
            // is always capped. The view tells the reader when the cap bit.
            if (maxRows <= 0 || maxRows > 2000)
                maxRows = 500;

            var query = _context.AuditLogs
                .Include(l => l.User)
                    .ThenInclude(u => u.Role)
                .Include(l => l.TargetAsset)
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(l => l.LogDateTime >= fromDate.Value.Date);

            if (toDate.HasValue)
                query = query.Where(l => l.LogDateTime < toDate.Value.Date.AddDays(1));

            if (userId.HasValue)
                query = query.Where(l => l.UserID == userId.Value);

            if (!string.IsNullOrWhiteSpace(actionContains))
                query = query.Where(l => l.ActionPerformed.Contains(actionContains));

            var logs = await query
                .OrderByDescending(l => l.LogDateTime)
                .Take(maxRows)
                .ToListAsync();

            return logs.Select(l => new AuditReportRowDTO
            {
                LogID = l.LogID,
                LogDateTime = l.LogDateTime,
                UserID = l.UserID,
                UserName = $"{l.User.FirstName} {l.User.LastName}",
                UserRole = l.User.Role.RoleName,
                ActionPerformed = l.ActionPerformed,
                TargetAssetID = l.TargetAssetID,
                TargetAssetCode = l.TargetAsset?.AssetCode,
                Description = l.Description,
                IPAddress = l.IPAddress
            }).ToList();
        }
    }
}