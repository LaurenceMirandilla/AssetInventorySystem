using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Constants;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    public class UserService : IUserService
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher;

        public UserService(ApplicationDbContext context)
        {
            _context = context;
            _passwordHasher = new PasswordHasher<User>();
        }

        private IQueryable<User> UserQueryWithIncludes()
        {
            return _context.Users
                .Include(u => u.Role)
                .Include(u => u.Department)
                .Include(u => u.Branch);
        }

        // One active Department Head per department: new-item requests go
        // to "the" department head, so two would split who approves.
        // Deactivated heads do not count, so a department can get a new
        // head once the old one is deactivated -- but reactivating the old
        // one then needs the new one moved or deactivated first.
        private async Task EnsureDepartmentHeadFreeAsync(int roleId, int departmentId, int exceptUserId)
        {
            var isHeadRole = await _context.Roles
                .AnyAsync(r => r.RoleID == roleId && r.RoleName == RoleNames.DepartmentHead);
            if (!isHeadRole)
                return;

            var currentHead = await _context.Users
                .Where(u => u.DepartmentID == departmentId
                            && u.UserID != exceptUserId
                            && u.Status == "Active"
                            && u.Role.RoleName == RoleNames.DepartmentHead)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync();

            if (currentHead is null)
                return;

            var department = await _context.Departments
                .Where(d => d.DepartmentID == departmentId)
                .Select(d => d.DepartmentName)
                .FirstOrDefaultAsync();

            throw new InvalidOperationException(
                $"{department ?? "This department"} already has a department head ({currentHead}). " +
                "A department can have only one. Change their role or deactivate them first.");
        }

        public async Task<UserResponseDTO> CreateUserAsync(CreateUserDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId);

            var emailExists = await _context.Users.AnyAsync(u => u.Email == dto.Email);
            if (emailExists)
                throw new InvalidOperationException("A user with this email already exists.");

            // The form only offers departments of the chosen branch, but that
            // filter is JavaScript and a stale or hand-built post skips it.
            // This is the check that actually keeps the pair consistent.
            var departmentInBranch = await _context.Departments
                .AnyAsync(d => d.DepartmentID == dto.DepartmentID && d.BranchID == dto.BranchID);
            if (!departmentInBranch)
                throw new ArgumentException("That department does not belong to the selected branch.");

            await EnsureDepartmentHeadFreeAsync(dto.RoleID, dto.DepartmentID, exceptUserId: 0);

            var user = new User
            {
                RoleID = dto.RoleID,
                DepartmentID = dto.DepartmentID,
                BranchID = dto.BranchID,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                Status = "Active",
                PasswordHash = string.Empty
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var created = await UserQueryWithIncludes().FirstAsync(u => u.UserID == user.UserID);
            return created.ToResponseDTO();
        }

        public async Task<UserResponseDTO?> GetUserByIdAsync(int userId)
        {
            var user = await UserQueryWithIncludes().FirstOrDefaultAsync(u => u.UserID == userId);
            return user?.ToResponseDTO();
        }

        public async Task<List<UserResponseDTO>> GetAllUsersAsync(string? status = null)
        {
            var query = UserQueryWithIncludes();

            // Filtering in the query rather than after materialising, so a
            // large Users table does not get pulled across just to be thrown
            // away. Blank means "no filter" -- see the interface comment.
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(u => u.Status == status);

            var users = await query.ToListAsync();
            return users.Select(u => u.ToResponseDTO()).ToList();
        }

        public async Task UpdateUserAsync(int userId, UpdateUserDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId);

            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");

            // Only an active user holds the post; an inactive one being
            // edited is checked again if they are reactivated.
            if (user.Status == "Active")
                await EnsureDepartmentHeadFreeAsync(dto.RoleID, dto.DepartmentID, exceptUserId: userId);

            _context.Entry(user).Property(u => u.RowVersion).OriginalValue = dto.RowVersion;

            user.RoleID = dto.RoleID;
            user.DepartmentID = dto.DepartmentID;
            user.BranchID = dto.BranchID;
            user.FirstName = dto.FirstName;
            user.LastName = dto.LastName;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This user was modified by someone else. Please reload and try again.");
            }
        }

        public async Task DeactivateUserAsync(int userId, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId);

            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");

            if (user.UserID == actingUserId)
                throw new InvalidOperationException("You cannot deactivate your own account.");

            _context.Entry(user).Property(u => u.RowVersion).OriginalValue = rowVersion;

            user.Status = "Inactive";

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This user was modified by someone else. Please reload and try again.");
            }
        }

        public async Task ReactivateUserAsync(int userId, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId);

            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");

            await EnsureDepartmentHeadFreeAsync(user.RoleID, user.DepartmentID, exceptUserId: userId);

            _context.Entry(user).Property(u => u.RowVersion).OriginalValue = rowVersion;

            user.Status = "Active";

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This user was modified by someone else. Please reload and try again.");
            }
        }
        public async Task<List<RoleDTO>> GetAllRolesAsync()
        {
            var roles = await _context.Roles.OrderBy(r => r.RoleName).ToListAsync();
            return roles.Select(r => r.ToDTO()).ToList();
        }

        // Administrative reset — Administrator or Principal only.
        //
        // Deliberately refuses to act on the acting user's own account. An
        // admin who has their own password still has ChangePassword, which
        // proves knowledge of the old one; routing self-changes through here
        // instead would mean an unattended signed-in session is enough to
        // rotate that admin's credential silently. An admin who has genuinely
        // lost their own password needs a second Administrator or the
        // Principal to reset it, which is the correct answer.
        public async Task ResetPasswordAsync(int userId, string newPassword, byte[] rowVersion, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId);

            if (userId == actingUserId)
                throw new InvalidOperationException(
                    "Use Change Password to set your own password.");

            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");

            // RowVersion is checked here for the same reason it is on every
            // other mutation: if two managers reset the same account at once,
            // one of them walks away having handed out a password that is no
            // longer live. Better to make the second one reload.
            _context.Entry(user).Property(u => u.RowVersion).OriginalValue = rowVersion;

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);

            // Security hygiene: an unexpected reset is exactly the thing the
            // account holder should find out about. It makes an unauthorised
            // one visible instead of silent. Note this is the ADMIN reset
            // path only -- ChangePasswordAsync is the holder doing it
            // themselves, who plainly does not need telling.
            NotificationHelper.Queue(
                _context,
                user.UserID,
                "Your password was reset by an administrator. " +
                "If you did not expect this, report it immediately.");

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "This user was modified by someone else. Please reload and try again.");
            }
        }

        public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user is null)
                throw new KeyNotFoundException("User not found.");

            var verifyResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword);
            if (verifyResult == PasswordVerificationResult.Failed)
                throw new UnauthorizedAccessException("Current password is incorrect.");

            user.PasswordHash = _passwordHasher.HashPassword(user, newPassword);

            await _context.SaveChangesAsync();
        }
    }
}