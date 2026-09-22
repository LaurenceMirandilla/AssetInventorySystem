using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Interfaces
{
    public interface IAuthService
    {
        Task<User?> ValidateCredentialsAsync(string email, string password);
    }
}