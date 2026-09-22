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
            return _context.Departments.Include(d => d.Branch);
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

            department.DepartmentName = dto.DepartmentName;
            department.Description = dto.Description;
            department.BranchID = dto.BranchID;

            await _context.SaveChangesAsync();
        }

        public async Task DeleteDepartmentAsync(int departmentId, int actingUserId)
        {
            await PermissionHelper.EnsureIsUserManagerAsync(_context, actingUserId, "departments");

            var department = await _context.Departments.FindAsync(departmentId);
            if (department is null)
                throw new KeyNotFoundException("Department not found.");

            var inUse = await _context.Users.AnyAsync(u => u.DepartmentID == departmentId)
                || await _context.Assets.AnyAsync(a => a.DepartmentID == departmentId)
                || await _context.AssetRequests.AnyAsync(ar => ar.DepartmentID == departmentId);

            if (inUse)
                throw new InvalidOperationException(
                    "This department cannot be deleted because it has users, assets, or asset requests under it.");

            _context.Departments.Remove(department);
            await _context.SaveChangesAsync();
        }
    }
}