using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.DTOs;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Mappings;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    // Same reasoning as BranchService: every User, Asset and AssetRequest
    // carries a DepartmentID, so who may reshape departments belongs with
    // whoever may manage users — Administrator or Principal. Reads stay open
    // because the assignment and approval screens both need the list.
    public class DepartmentService : IDepartmentService
    {
        private readonly ApplicationDbContext _context;

        public DepartmentService(ApplicationDbContext context)
        {
            _context = context;
        }

        private IQueryable<Department> DepartmentQueryWithIncludes()
        {
            return _context.Departments.Include(d => d.Branch).Include(d => d.Rooms);
        }

        // A department's rooms (Scripts/029): at least one, every one in its
        // branch and not another department's. Rooms taken off it are free
        // again. The main room is the one picked, else the first.
        private async Task SetRoomsAsync(Department department, List<int>? locationIds, int? mainLocationId)
        {
            var ids = (locationIds ?? new List<int>()).Distinct().ToList();
            if (ids.Count == 0)
                throw new ArgumentException("Pick at least one room for the department.");

            var rooms = await _context.Locations.Include(l => l.Department)
                .Where(l => ids.Contains(l.LocationID)).ToListAsync();
            if (rooms.Count != ids.Count)
                throw new KeyNotFoundException("A room was not found.");
            var elsewhere = rooms.FirstOrDefault(l => l.BranchID != department.BranchID);
            if (elsewhere is not null)
                throw new ArgumentException($"{elsewhere.LocationName.Trim()} is in another branch. Pick rooms in the department's own branch.");
            var taken = rooms.FirstOrDefault(l => l.DepartmentID is int owner && owner != department.DepartmentID && owner != 0);
            if (taken is not null)
                throw new InvalidOperationException(
                    $"{taken.LocationName.Trim()} belongs to the {taken.Department!.DepartmentName}. A room belongs to one department only: take it off that department first.");

            if (department.DepartmentID != 0)
            {
                var dropped = await _context.Locations
                    .Where(l => l.DepartmentID == department.DepartmentID && !ids.Contains(l.LocationID))
                    .ToListAsync();
                foreach (var room in dropped)
                    room.DepartmentID = null;
            }
            foreach (var room in rooms)
                room.Department = department;

            var main = mainLocationId is int m && ids.Contains(m) ? m : ids[0];
            department.MainLocation = rooms.First(l => l.LocationID == main);
        }

        public async Task<DepartmentDTO> CreateDepartmentAsync(CreateDepartmentDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "departments");

            // Verify branch exists
            var branchExists = await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID);
            if (!branchExists)
                throw new KeyNotFoundException("The specified branch does not exist.");

            // Check for duplicate name within the same branch
            var nameExists = await _context.Departments.AnyAsync(d => d.DepartmentName == dto.DepartmentName && d.BranchID == dto.BranchID);
            if (nameExists)
                throw new InvalidOperationException("A department with this name already exists in this branch.");

            var department = new Department
            {
                DepartmentName = dto.DepartmentName,
                Description = dto.Description,
                BranchID = dto.BranchID
            };

            // A new department needs its rooms.
            await SetRoomsAsync(department, dto.LocationIDs, dto.MainLocationID);

            _context.Departments.Add(department);
            await _context.SaveChangesAsync();

            // Reload with includes for mapping
            department = await DepartmentQueryWithIncludes().FirstAsync(d => d.DepartmentID == department.DepartmentID);
            return department.ToDTO();
        }

        public async Task<DepartmentDTO?> GetDepartmentByIdAsync(int departmentId)
        {
            var department = await DepartmentQueryWithIncludes().FirstOrDefaultAsync(d => d.DepartmentID == departmentId);
            return department?.ToDTO();
        }

        public async Task<List<DepartmentDTO>> GetAllDepartmentsAsync()
        {
            var departments = await DepartmentQueryWithIncludes().OrderBy(d => d.Branch.BranchName).ThenBy(d => d.DepartmentName).ToListAsync();
            return departments.Select(d => d.ToDTO()).ToList();
        }

        public async Task<List<DepartmentDTO>> GetDepartmentsByBranchAsync(int branchId)
        {
            var departments = await DepartmentQueryWithIncludes()
                .Where(d => d.BranchID == branchId)
                .OrderBy(d => d.DepartmentName)
                .ToListAsync();
            return departments.Select(d => d.ToDTO()).ToList();
        }

        public async Task UpdateDepartmentAsync(int departmentId, UpdateDepartmentDTO dto, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "departments");

            var department = await _context.Departments.FindAsync(departmentId);
            if (department is null)
                throw new KeyNotFoundException("Department not found.");

            // Verify branch exists
            var branchExists = await _context.Branches.AnyAsync(b => b.BranchID == dto.BranchID);
            if (!branchExists)
                throw new KeyNotFoundException("The specified branch does not exist.");

            // Check for duplicate name within the same branch (excluding current department)
            var nameExists = await _context.Departments.AnyAsync(d => d.DepartmentName == dto.DepartmentName && d.BranchID == dto.BranchID && d.DepartmentID != departmentId);
            if (nameExists)
                throw new InvalidOperationException("A department with this name already exists in this branch.");

            // The Property Department keeps its branch's stock: it can be
            // renamed, not moved.
            if (department.IsProperty && dto.BranchID != department.BranchID)
                throw new InvalidOperationException(
                    "The Property Department keeps this branch's stock, so it cannot be moved to another branch.");

            department.DepartmentName = dto.DepartmentName;
            department.Description = dto.Description;
            department.BranchID = dto.BranchID;
            await SetRoomsAsync(department, dto.LocationIDs, dto.MainLocationID);

            await _context.SaveChangesAsync();
        }

        public async Task DeleteDepartmentAsync(int departmentId, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "departments");

            var department = await _context.Departments.FindAsync(departmentId);
            if (department is null)
                throw new KeyNotFoundException("Department not found.");
            if (department.IsProperty)
                throw new InvalidOperationException(
                    "The Property Department keeps this branch's stock and cannot be deleted.");

            var inUse = await _context.Users.AnyAsync(u => u.DepartmentID == departmentId)
                || await _context.Assets.AnyAsync(a => a.DepartmentID == departmentId)
                || await _context.AssetRequests.AnyAsync(ar => ar.DepartmentID == departmentId)
                || await _context.NewItemRequests.AnyAsync(r => r.DepartmentID == departmentId)
                || await _context.SupplyRequests.AnyAsync(r => r.DepartmentID == departmentId)
                // Supplies it keeps, and their history.
                || await _context.SupplyStock.AnyAsync(k => k.DepartmentID == departmentId)
                || await _context.SupplyTransactions.AnyAsync(t => t.DepartmentID == departmentId)
                // Units out on a borrow or transfer that were taken from it.
                || await _context.AssetAssignments.AnyAsync(a => a.ReturnDate == null && a.PreviousDepartmentID == departmentId);

            if (inUse)
                throw new InvalidOperationException(
                    "This department cannot be deleted because it has users, assets (including ones out on loan), supplies, or requests under it.");

            // Its rooms are free again.
            department.MainLocationID = null;
            foreach (var room in await _context.Locations.Where(l => l.DepartmentID == departmentId).ToListAsync())
                room.DepartmentID = null;
            await _context.SaveChangesAsync();

            _context.Departments.Remove(department);
            await _context.SaveChangesAsync();
        }
    }
}