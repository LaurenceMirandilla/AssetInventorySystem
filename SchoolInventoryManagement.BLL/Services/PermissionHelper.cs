using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    internal static class PermissionHelper
    {
        public static async Task<User> GetUserOrThrowAsync(ApplicationDbContext context, int userId)
        {
            var user = await context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserID == userId);

            if (user is null)
                throw new UnauthorizedAccessException("User not found.");

            return user;
        }

        // Every Ensure* helper below checks that the ACTING user is Active,
        // not just that their role is right.
        //
        // Deactivation is this project's substitute for deleting a person, so
        // it has to take effect at once. AuthService already refuses an
        // inactive account at sign-in, but the auth cookie lasts 8 hours with
        // sliding expiration -- so without this check, deactivating someone
        // mid-session left them able to keep approving requests, managing
        // users and reading reports until that cookie happened to expire.
        //
        // EnsureIsActive already existed and was applied to the RECIPIENT of
        // an assignment; it was simply never applied to the actor. Found by
        // PermissionHelperInactiveUserTests.
        public static async Task<User> EnsureIsAssetManagerAsync(ApplicationDbContext context, int userId)
        {
            var user = await GetUserOrThrowAsync(context, userId);
            EnsureIsActive(user);

            if (user.Role.RoleName != RoleNames.AssetOfficer &&
                user.Role.RoleName != RoleNames.Administrator)
            {
                throw new UnauthorizedAccessException(
                    "Only Asset Officers or Administrators can perform this action.");
            }

            return user;
        }

        public static readonly string[] ApproverRoles =
        {
            RoleNames.AssetOfficer,
            RoleNames.Administrator,
            RoleNames.Principal
        };

        public static async Task<User> EnsureIsApproverAsync(ApplicationDbContext context, int userId)
        {
            var user = await GetUserOrThrowAsync(context, userId);
            EnsureIsActive(user);

            if (Array.IndexOf(ApproverRoles, user.Role.RoleName) < 0)
            {
                throw new UnauthorizedAccessException(
                    "Only Asset Officers, Administrators, or Principals can perform this action.");
            }

            return user;
        }

        // Same three roles as ApproverRoles today, but kept as its own list
        // deliberately: "may decide on a request" and "may read reports" are
        // different privileges that happen to coincide right now. A
        // Department Head could reasonably gain reporting without gaining
        // approval, and this is the seam where that would happen.
        public static readonly string[] ReportViewerRoles =
        {
            RoleNames.AssetOfficer,
            RoleNames.Administrator,
            RoleNames.Principal
        };

        public static async Task<User> EnsureCanViewReportsAsync(ApplicationDbContext context, int userId)
        {
            var user = await GetUserOrThrowAsync(context, userId);
            EnsureIsActive(user);

            if (Array.IndexOf(ReportViewerRoles, user.Role.RoleName) < 0)
            {
                throw new UnauthorizedAccessException(
                    "Only Asset Officers, Administrators, or Principals can view reports.");
            }

            return user;
        }

        public static void EnsureIsActive(User user)
        {
            if (user.Status != "Active")
                throw new InvalidOperationException($"User '{user.Email}' is not active.");
        }
        // Covers user/role/branch/department management — everything the
        // permission matrix puts behind Administrator or Principal.
        //
        // `subject` only shapes the error message; the rule is identical for
        // every caller. It exists so a rejected branch edit doesn't tell the
        // user they can't "manage users", which would be baffling. It is
        // optional, so the existing two-argument calls in UserService keep
        // working unchanged. Adding a second near-identical Ensure* method
        // for this would be exactly the duplication this helper exists to
        // prevent.
        public static async Task<User> EnsureIsUserManagerAsync(
            ApplicationDbContext context, int userId, string subject = "users")
        {
            var user = await GetUserOrThrowAsync(context, userId);
            EnsureIsActive(user);

            if (user.Role.RoleName != RoleNames.Administrator &&
                user.Role.RoleName != RoleNames.Principal)
            {
                throw new UnauthorizedAccessException(
                    $"Only Administrators or Principals can manage {subject}.");
            }

            return user;
        }
    }
}