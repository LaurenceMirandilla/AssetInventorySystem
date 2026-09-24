using System.Collections.Generic;
using System.Threading.Tasks;
using SchoolInventoryManagement.BLL.DTOs;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IUserService
    {
        Task<UserResponseDTO> CreateUserAsync(CreateUserDTO dto, int actingUserId);
        Task<UserResponseDTO?> GetUserByIdAsync(int userId);
        // status filters on Users.Status ("Active"/"Inactive"); null or blank
        // returns everyone. Optional so existing call sites keep compiling and
        // keep their previous behaviour.
        Task<List<UserResponseDTO>> GetAllUsersAsync(string? status = null);

        Task UpdateUserAsync(int userId, UpdateUserDTO dto, int actingUserId);
        Task DeactivateUserAsync(int userId, byte[] rowVersion, int actingUserId);
        Task ReactivateUserAsync(int userId, byte[] rowVersion, int actingUserId);
        Task ChangePasswordAsync(int userId, string currentPassword, string newPassword);

        // Administrative reset. Distinct from ChangePasswordAsync because it
        // cannot ask for the current password — the whole point is that the
        // account holder has lost it. It pays for that with a role check the
        // self-service path does not have.
        Task ResetPasswordAsync(int userId, string newPassword, byte[] rowVersion, int actingUserId);

        // Roles are fixed, seeded reference data — there is no RoleService
        // and nothing creates or edits them, so the one thing anybody needs
        // is the list, for the user form's dropdown.
        Task<List<RoleDTO>> GetAllRolesAsync();
    }
}