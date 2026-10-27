using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // Branches are org structure, not asset data: the permission matrix puts
    // them behind Administrator or Principal, alongside user management,
    // because every User row carries a BranchID. Reads stay open — the whole
    // app needs branch dropdowns.
    public class BranchService : IBranchService
    {
        private readonly ApplicationDbContext _context;

        public BranchService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<BranchDTO> CreateBranchAsync(CreateBranchDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "branches");

            var nameExists = await _context.Branches.AnyAsync(b => b.BranchName == dto.BranchName);
            if (nameExists)
                throw new System.InvalidOperationException("A branch with this name already exists.");

            var code = NormalizeCode(dto.BranchCode);
            await EnsureCodeFreeAsync(code, exceptBranchId: null);

            var branch = new Branch
            {
                BranchName = dto.BranchName,
                BranchCode = code,
                Address = dto.Address,
                ContactInfo = dto.ContactInfo
            };

            _context.Branches.Add(branch);
            await _context.SaveChangesAsync();

            return branch.ToDTO();
        }

        public async Task<BranchDTO?> GetBranchByIdAsync(int branchId)
        {
            var branch = await _context.Branches.FindAsync(branchId);
            return branch?.ToDTO();
        }

        public async Task<List<BranchDTO>> GetAllBranchesAsync()
        {
            var branches = await _context.Branches.OrderBy(b => b.BranchName).ToListAsync();
            return branches.Select(b => b.ToDTO()).ToList();
        }

        public async Task UpdateBranchAsync(int branchId, UpdateBranchDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "branches");

            var branch = await _context.Branches.FindAsync(branchId);
            if (branch is null)
                throw new KeyNotFoundException("Branch not found.");

            var nameExists = await _context.Branches.AnyAsync(b => b.BranchName == dto.BranchName && b.BranchID != branchId);
            if (nameExists)
                throw new System.InvalidOperationException("A branch with this name already exists.");

            // Same rule as location codes: existing asset codes keep the
            // old letter, so it is locked once the branch has assets.
            var code = NormalizeCode(dto.BranchCode);
            if (code != branch.BranchCode && await HasAssetsAsync(branchId))
                throw new System.InvalidOperationException(
                    "The branch code cannot be changed once the branch has assets.");
            await EnsureCodeFreeAsync(code, exceptBranchId: branchId);

            branch.BranchName = dto.BranchName;
            branch.BranchCode = code;
            branch.Address = dto.Address;
            branch.ContactInfo = dto.ContactInfo;

            await _context.SaveChangesAsync();
        }

        public async Task<bool> HasAssetsAsync(int branchId)
        {
            return await _context.Assets.AnyAsync(a => a.BranchID == branchId);
        }

        // Upper-case, exactly one letter. The DTO checks the same thing;
        // this is the check a hand-built post cannot skip.
        private static string NormalizeCode(string? raw)
        {
            var code = (raw ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(code, "^[A-Z]$"))
                throw new System.ArgumentException("The branch code must be exactly 1 letter, e.g. M.");
            return code;
        }

        private async Task EnsureCodeFreeAsync(string code, int? exceptBranchId)
        {
            var owner = await _context.Branches
                .Where(b => b.BranchCode == code && b.BranchID != exceptBranchId)
                .Select(b => b.BranchName)
                .FirstOrDefaultAsync();

            if (owner is not null)
                throw new System.InvalidOperationException($"The code {code} is already used by {owner}.");
        }

        public async Task DeleteBranchAsync(int branchId, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "branches");

            var branch = await _context.Branches.FindAsync(branchId);
            if (branch is null)
                throw new KeyNotFoundException("Branch not found.");

            // Note: locations are managed by Asset Officers, not by the roles
            // allowed in here — so a Principal can be blocked by locations
            // they cannot remove themselves. That is deliberate; deleting an
            // entire branch should need more than one person's say-so.
            var inUse = await _context.Departments.AnyAsync(d => d.BranchID == branchId)
                || await _context.Locations.AnyAsync(l => l.BranchID == branchId)
                || await _context.Users.AnyAsync(u => u.BranchID == branchId)
                || await _context.Assets.AnyAsync(a => a.BranchID == branchId);

            if (inUse)
                throw new InvalidOperationException(
                    "This branch cannot be deleted because it has departments, locations, users, or assets under it.");

            _context.Branches.Remove(branch);
            await _context.SaveChangesAsync();
        }
    }
}