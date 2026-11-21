using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities.Enums;

namespace SchoolInventoryManagement.BLL.Services
{
    public class LiveStatusService : ILiveStatusService
    {
        private readonly ApplicationDbContext _context;

        public LiveStatusService(ApplicationDbContext context)
        {
            _context = context;
        }

        private static readonly string[] ApproverRoles =
            { RoleNames.Administrator, RoleNames.Principal, RoleNames.AssetOfficer };

        private Task<(int RoleID, string RoleName)?> RoleOfAsync(int userId) =>
            _context.Users.AsNoTracking()
                .Where(u => u.UserID == userId && u.Status == "Active")
                .Select(u => new ValueTuple<int, string>(u.RoleID, u.Role.RoleName))
                .Cast<(int, string)?>()
                .FirstOrDefaultAsync();

        public async Task<LiveStatusDTO> GetStatusAsync(int userId, string? scope)
        {
            var status = new LiveStatusDTO
            {
                Unread = await _context.Notifications.CountAsync(n => n.UserID == userId && !n.IsRead),
                Stamp = await StampOrNullAsync(userId, scope),
                Latest = await _context.Notifications.AsNoTracking()
                    .Where(n => n.UserID == userId && !n.IsRead)
                    .OrderByDescending(n => n.NotificationID)
                    .Select(n => new LiveNotificationDTO { Id = n.NotificationID, Message = n.Message })
                    .FirstOrDefaultAsync()
            };

            var role = await RoleOfAsync(userId);
            if (role is { } r && ApproverRoles.Contains(r.RoleName))
                status.Approvals = await CountApprovalsAsync(userId);
            return status;
        }

        // The counts still go out if the stamp cannot be worked out; the
        // page then just does not refresh itself.
        private async Task<string?> StampOrNullAsync(int userId, string? scope)
        {
            try { return await GetStampAsync(userId, scope); }
            catch (Exception) { return null; }
        }

        public async Task<int> GetLatestNotificationIdAsync(int userId) =>
            await _context.Notifications.Where(n => n.UserID == userId)
                .MaxAsync(n => (int?)n.NotificationID) ?? 0;

        public async Task<int> CountApprovalsAsync(int userId)
        {
            var role = await RoleOfAsync(userId);
            if (role is not { } r || !ApproverRoles.Contains(r.RoleName))
                return 0;

            // Same rule as the Approvals page: nobody decides a request from
            // someone with their own role, so those are not counted. Borrow
            // and transfer requests are Property's (Property Officers, with
            // Administrators as a backup), so the Principal counts supplies
            // only.
            var pending = 0;
            if (r.RoleName == RoleNames.AssetOfficer || r.RoleName == RoleNames.Administrator)
                pending += await _context.AssetRequests.CountAsync(q =>
                    q.RequestStatus == RequestStatus.Pending && q.RequestedByUser.RoleID != r.RoleID);
            pending += await _context.SupplyRequests.CountAsync(q =>
                q.Status == SupplyRequestStatus.Pending
                && q.RequestedByUserID != userId
                && q.RequestedBy.RoleID != r.RoleID);
            return pending;
        }

        // ---- Stamps ----------------------------------------------------
        //
        // A stamp is a few numbers per table: how many rows, and the newest
        // change. SQL Server bumps a row's RowVersion on every insert and
        // update, so MAX(RowVersion) moves whenever any row changes; the
        // count catches deletes. Tables without a RowVersion use their
        // newest id instead. {0} is the user, {1} the id in the scope.

        private static string Rv(string table, string where = "1 = 1") =>
            $"(SELECT CONCAT(COUNT_BIG(*), ':', CONVERT(bigint, MAX(RowVersion))) FROM dbo.{table} WHERE {where})";

        private static string Ids(string table, string idColumn, string where = "1 = 1") =>
            $"(SELECT CONCAT(COUNT_BIG(*), ':', MAX({idColumn})) FROM dbo.{table} WHERE {where})";

        private const string MyNotifications =
            "(SELECT CONCAT(COUNT_BIG(*), ':', MAX(NotificationID), ':', SUM(CASE WHEN IsRead = 0 THEN 1 ELSE 0 END)) FROM dbo.Notifications WHERE UserID = {0})";

        private static readonly string[] Approvals =
            { Rv("AssetRequests"), Rv("AssetAssignments"), Rv("SupplyRequests") };
        private static readonly string[] NewItems =
            { Rv("NewItemRequests"), Ids("NewItemRequestSteps", "StepID") };
        private static readonly string[] Mine =
        {
            Rv("AssetRequests", "RequestedByUserID = {0}"),
            Rv("AssetAssignments", "AssignedToUserID = {0}"),
            Rv("SupplyRequests", "RequestedByUserID = {0}"),
            Rv("NewItemRequests", "RequestedByUserID = {0}"),
            Rv("Assets", "AssignedUserID = {0}")
        };
        private static readonly string[] Assets = { Rv("Assets") };
        private static readonly string[] Supplies =
        {
            Rv("SupplyStock"),
            Ids("Supplies", "SupplyID"),
            Ids("SupplyTransactions", "SupplyTransactionID"),
            Ids("SupplyBranchLevels", "SupplyBranchLevelID")
        };

        public async Task<string?> GetStampAsync(int userId, string? scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
                return null;

            var role = await RoleOfAsync(userId);
            if (role is not { } r)
                return null;
            var approver = ApproverRoles.Contains(r.RoleName);
            var manager = r.RoleName == RoleNames.AssetOfficer || r.RoleName == RoleNames.Administrator;

            // "assetrequest:5" -> ("assetrequest", 5)
            var name = scope.Trim().ToLowerInvariant();
            var id = 0;
            var colon = name.IndexOf(':');
            if (colon > 0)
            {
                if (!int.TryParse(name[(colon + 1)..], out id))
                    return null;
                name = name[..colon];
            }

            // Someone else's request: only people who may act on it get a
            // stamp for it.
            var own = approver ? "1 = 1" : "RequestedByUserID = {0}";

            IEnumerable<string>? parts = name switch
            {
                "approvals" => Approvals,
                "newitems" => NewItems,
                "history" => manager ? Approvals : new[] { Rv("AssetRequests", "RequestedByUserID = {0}") },
                "mine" => Mine,
                "assets" => Assets,
                "supplies" => Supplies,
                "notifications" => new[] { MyNotifications },
                "home" => approver ? Approvals.Concat(NewItems).Concat(Assets).Concat(Supplies) : Mine,
                "assetrequest" => new[] { Rv("AssetRequests", "RequestID = {1} AND " + own), Rv("AssetAssignments", "RequestID = {1}") },
                "supplyrequest" => new[] { Rv("SupplyRequests", "SupplyRequestID = {1} AND " + own) },
                "newitem" => new[] { Rv("NewItemRequests", "NewItemRequestID = {1} AND " + own), Ids("NewItemRequestSteps", "StepID", "NewItemRequestID = {1}") },
                "asset" => new[] { Rv("Assets", "AssetID = {1}"), Rv("AssetAssignments", "AssetID = {1}"), Ids("AssetMovements", "MovementID", "AssetID = {1}") },
                "supply" => new[] { Rv("SupplyStock", "SupplyID = {1}"), Ids("SupplyTransactions", "SupplyTransactionID", "SupplyID = {1}"), Ids("SupplyBranchLevels", "SupplyBranchLevelID", "SupplyID = {1}") },
                _ => null
            };
            if (parts is null)
                return null;

            // CONCAT needs at least two arguments, hence the trailing ''.
            var sql = "SELECT CONCAT(" + string.Join(", '|', ", parts) + ", '') AS [Value]";
            var rows = await _context.Database.SqlQueryRaw<string>(sql, userId, id).ToListAsync();
            return rows.FirstOrDefault();
        }
    }
}
