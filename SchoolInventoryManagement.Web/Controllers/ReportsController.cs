using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities.Enums;
using SchoolInventoryManagement.Web.Helpers;

namespace SchoolInventoryManagement.Web.Controllers
{
    // Reports are read-only, so there is nothing here to guard with
    // RowVersion and nothing that can throw ConcurrencyConflictException.
    // Filters arrive as plain action parameters rather than view models,
    // matching AssetsController.Index — they are all optional and none of
    // them are posted back.
    [Authorize(Roles = RoleNames.AssetOfficer + "," + RoleNames.Administrator + "," + RoleNames.Principal)]
    public class ReportsController : BaseController
    {
        private readonly IReportService _reportService;
        private readonly IDepartmentService _departmentService;
        private readonly IBranchService _branchService;
        private readonly IUserService _userService;

        public ReportsController(
            IReportService reportService,
            IDepartmentService departmentService,
            IBranchService branchService,
            IUserService userService)
        {
            _reportService = reportService;
            _departmentService = departmentService;
            _branchService = branchService;
            _userService = userService;
        }

        private int CurrentUserId =>
            int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ==================================================================
        // Dashboard
        // ==================================================================

        // GET /Reports
        public async Task<IActionResult> Index()
        {
            var summary = await _reportService.GetInventorySummaryAsync(CurrentUserId);
            return View(summary);
        }

        // ==================================================================
        // Outstanding assignments
        // ==================================================================

        // GET /Reports/Assignments
        public async Task<IActionResult> Assignments(int? departmentId, int? branchId)
        {
            await PopulateScopeDropdownsAsync(departmentId, branchId);

            var rows = await _reportService.GetOutstandingAssignmentsAsync(
                departmentId, branchId, CurrentUserId);

            return View(rows);
        }

        // GET /Reports/ExportAssignments
        public async Task<IActionResult> ExportAssignments(int? departmentId, int? branchId)
        {
            var rows = await _reportService.GetOutstandingAssignmentsAsync(
                departmentId, branchId, CurrentUserId);

            var csv = CsvExportHelper.Build(
                new[]
                {
                    "Asset Code", "Asset Name", "Assigned To", "Email", "Role",
                    "Department", "Assigned By", "Assignment Date", "Days Out",
                    "Condition Out", "Remarks"
                },
                rows.Select(r => new string?[]
                {
                    r.AssetCode,
                    r.AssetName,
                    r.AssignedToName,
                    r.AssignedToEmail,
                    r.AssignedToRole,
                    r.DepartmentName,
                    r.AssignedByName,
                    r.AssignmentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.DaysOut.ToString(CultureInfo.InvariantCulture),
                    r.ConditionOnAssignment.ToString(),
                    r.Remarks
                }));

            return File(csv, "text/csv", CsvExportHelper.TimestampedFileName("outstanding-assignments"));
        }

        // ==================================================================
        // Disposals
        // ==================================================================

        // GET /Reports/Disposals
        public async Task<IActionResult> Disposals(DateTime? fromDate, DateTime? toDate, bool includeRestored = false)
        {
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.IncludeRestored = includeRestored;

            var rows = await _reportService.GetDisposalsAsync(
                fromDate, toDate, includeRestored, CurrentUserId);

            return View(rows);
        }

        // GET /Reports/ExportDisposals
        public async Task<IActionResult> ExportDisposals(DateTime? fromDate, DateTime? toDate, bool includeRestored = false)
        {
            var rows = await _reportService.GetDisposalsAsync(
                fromDate, toDate, includeRestored, CurrentUserId);

            var csv = CsvExportHelper.Build(
                new[]
                {
                    "Asset Code", "Asset Name", "Category", "Disposal Date",
                    "Reason", "Method", "Approved By", "Acquisition Cost",
                    "Restored", "Restored Date", "Restored By"
                },
                rows.Select(r => new string?[]
                {
                    r.AssetCode,
                    r.AssetName,
                    r.CategoryName,
                    r.DisposalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.ReasonForDisposal,
                    r.DisposalMethod,
                    r.ApprovedByName,
                    r.AcquisitionCost?.ToString("0.00", CultureInfo.InvariantCulture),
                    r.IsRestored ? "Yes" : "No",
                    r.RestoredDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.RestoredByName
                }));

            return File(csv, "text/csv", CsvExportHelper.TimestampedFileName("disposals"));
        }

        // ==================================================================
        // Request activity
        // ==================================================================

        // GET /Reports/Requests
        public async Task<IActionResult> Requests(
            DateTime? fromDate, DateTime? toDate, RequestStatus? status, RequestType? type)
        {
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.Status = status;
            ViewBag.Type = type;

            var report = await _reportService.GetRequestActivityAsync(
                fromDate, toDate, status, type, CurrentUserId);

            return View(report);
        }

        // GET /Reports/ExportRequests
        public async Task<IActionResult> ExportRequests(
            DateTime? fromDate, DateTime? toDate, RequestStatus? status, RequestType? type)
        {
            var report = await _reportService.GetRequestActivityAsync(
                fromDate, toDate, status, type, CurrentUserId);

            var csv = CsvExportHelper.Build(
                new[]
                {
                    "Request #", "Type", "Status", "Requester", "Requester Role",
                    "Department", "Item", "Reason", "Submitted", "Decided",
                    "Decided By", "Turnaround (days)", "Remarks"
                },
                report.Rows.Select(r => new string?[]
                {
                    r.RequestID.ToString(CultureInfo.InvariantCulture),
                    r.RequestType.ToString(),
                    r.RequestStatus.ToString(),
                    r.RequesterName,
                    r.RequesterRole,
                    r.DepartmentName,
                    r.ItemDescription,
                    r.Reason,
                    r.RequestDate.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.ApprovalDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.DecidedByName,
                    r.TurnaroundDays?.ToString("0.0", CultureInfo.InvariantCulture),
                    r.Remarks
                }));

            return File(csv, "text/csv", CsvExportHelper.TimestampedFileName("request-activity"));
        }

        // ==================================================================
        // Audit trail
        // ==================================================================

        // GET /Reports/Audit
        public async Task<IActionResult> Audit(
            DateTime? fromDate, DateTime? toDate, int? userId, string? action, int maxRows = 500)
        {
            ViewBag.FromDate = fromDate;
            ViewBag.ToDate = toDate;
            ViewBag.Action = action;
            ViewBag.MaxRows = maxRows;

            await PopulateUserDropdownAsync(userId);

            var rows = await _reportService.GetAuditTrailAsync(
                fromDate, toDate, userId, action, maxRows, CurrentUserId);

            // The service caps the row count; tell the reader when the cap
            // actually bit, so a truncated report is never mistaken for a
            // complete one.
            ViewBag.WasTruncated = rows.Count >= (maxRows > 0 && maxRows <= 2000 ? maxRows : 500);

            return View(rows);
        }

        // GET /Reports/ExportAudit
        public async Task<IActionResult> ExportAudit(
            DateTime? fromDate, DateTime? toDate, int? userId, string? action, int maxRows = 2000)
        {
            var rows = await _reportService.GetAuditTrailAsync(
                fromDate, toDate, userId, action, maxRows, CurrentUserId);

            var csv = CsvExportHelper.Build(
                new[]
                {
                    "Timestamp", "User", "Role", "Action", "Asset Code",
                    "Details", "IP Address"
                },
                rows.Select(r => new string?[]
                {
                    r.LogDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    r.UserName,
                    r.UserRole,
                    r.ActionPerformed,
                    r.TargetAssetCode,
                    r.Description,
                    r.IPAddress
                }));

            return File(csv, "text/csv", CsvExportHelper.TimestampedFileName("audit-trail"));
        }

        // ==================================================================
        // Dropdowns
        // ==================================================================

        private async Task PopulateScopeDropdownsAsync(int? selectedDepartmentId, int? selectedBranchId)
        {
            var branches = await _branchService.GetAllBranchesAsync();
            ViewBag.Branches = new SelectList(branches, "BranchID", "BranchName", selectedBranchId);

            // Department names repeat across branches, so label with both.
            var departments = await _departmentService.GetAllDepartmentsAsync();
            ViewBag.Departments = new SelectList(
                departments.Select(d => new
                {
                    d.DepartmentID,
                    Label = $"{d.BranchName} - {d.DepartmentName}"
                }),
                "DepartmentID", "Label", selectedDepartmentId);
        }

        private async Task PopulateUserDropdownAsync(int? selectedUserId)
        {
            // Inactive users stay listed here on purpose: their past actions
            // are still in the audit trail and still worth filtering to.
            var users = await _userService.GetAllUsersAsync();

            ViewBag.Users = new SelectList(
                users
                    .OrderBy(u => u.FirstName)
                    .ThenBy(u => u.LastName)
                    .Select(u => new
                    {
                        u.UserID,
                        Label = $"{u.FirstName} {u.LastName} ({u.RoleName})"
                    }),
                "UserID", "Label", selectedUserId);
        }
    }
}
