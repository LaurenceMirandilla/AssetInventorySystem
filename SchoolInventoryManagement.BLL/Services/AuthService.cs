using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.BLL.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher<User> _passwordHasher;

        public AuthService(ApplicationDbContext context)
        {
            _context = context;
            _passwordHasher = new PasswordHasher<User>();
        }

        public async Task<User?> ValidateCredentialsAsync(string email, string password)
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.Department)
                .Include(u => u.Branch)
                .FirstOrDefaultAsync(u => u.Email == email);

            if (user is null)
                return null;

            // Soft-delete check — inactive accounts can't log in
            if (user.Status != "Active")
                return null;

            var result = _passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, password);

            if (result == PasswordVerificationResult.Failed)
                return null;

            return user;
        }
    }
}