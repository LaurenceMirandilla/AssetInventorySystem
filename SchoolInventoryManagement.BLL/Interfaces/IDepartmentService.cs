using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IDepartmentService
    {
        Task<DepartmentDTO> CreateDepartmentAsync(CreateDepartmentDTO dto, int actingUserId);
        Task<DepartmentDTO?> GetDepartmentByIdAsync(int departmentId);
        Task<List<DepartmentDTO>> GetAllDepartmentsAsync();
        Task UpdateDepartmentAsync(int departmentId, UpdateDepartmentDTO dto, int actingUserId);
        Task DeleteDepartmentAsync(int departmentId, int actingUserId);
    }
}
