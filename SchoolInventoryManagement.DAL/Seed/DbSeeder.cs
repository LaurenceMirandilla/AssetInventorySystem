using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Entities;
using SchoolInventoryManagement.DAL.Constants;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;


namespace SchoolInventoryManagement.DAL.Seed
{
    public static class DbSeeder
    {
        // initialAdminPassword comes from configuration (Seed:AdminPassword).
        // When it is null/blank a cryptographically random password is
        // generated and logged ONCE, at the moment the account is created.
        public static async Task SeedAsync(
            ApplicationDbContext context,
            string? initialAdminPassword = null,
            ILogger? logger = null)
        {
            // ===== 1. Seed Roles =====
            if (!await context.Roles.AnyAsync())
            {
                // RoleNames, not literals: PermissionHelper and every
                // [Authorize(Roles = ...)] attribute compare against these
                // constants, so a typo in a literal here would seed a role
                // that nothing can ever match.
                context.Roles.AddRange(
                    new Role { RoleName = RoleNames.Administrator },
                    new Role { RoleName = RoleNames.AssetOfficer },
                    new Role { RoleName = RoleNames.Teacher },
                    new Role { RoleName = RoleNames.Staff },
                    new Role { RoleName = RoleNames.DepartmentHead },
                    new Role { RoleName = RoleNames.Principal }
                );
                await context.SaveChangesAsync();
            }

            // ===== 2. Seed a default Branch (Users/Departments require one) =====
            if (!await context.Branches.AnyAsync())
            {
                context.Branches.Add(new Branch
                {
                    BranchName = "Main Campus",
                    Address = "Default Address",
                    ContactInfo = "N/A"
                });
                await context.SaveChangesAsync();
            }

            // ===== 3. Seed a default Department (Users require one) =====
            if (!await context.Departments.AnyAsync())
            {
                var branch = await context.Branches.FirstAsync();
                context.Departments.Add(new Department
                {
                    BranchID = branch.BranchID,
                    DepartmentName = "Administration",
                    Description = "Default administrative department"
                });
                await context.SaveChangesAsync();
            }

            // ===== 4. Seed the Administrator user =====
            if (!await context.Users.AnyAsync())
            {
                var adminRole = await context.Roles.FirstAsync(r => r.RoleName == "Administrator");
                var branch = await context.Branches.FirstAsync();
                var department = await context.Departments.FirstAsync();

                var admin = new User
                {
                    RoleID = adminRole.RoleID,
                    DepartmentID = department.DepartmentID,
                    BranchID = branch.BranchID,
                    FirstName = "System",
                    LastName = "Administrator",
                    Email = "admin@schoolinventory.local",
                    Status = "Active",
                    PasswordHash = string.Empty // placeholder, hashed below
                };

                // The initial administrator password is deliberately NOT
                // hard-coded. A literal here ships the SAME known credential
                // to every deployment, and it stays readable in git history
                // long after anyone rotates it. It now comes from
                // configuration; if that is unset we generate a random one
                // rather than fall back to a guessable default, so a fresh
                // environment still works but never boots with a password an
                // attacker can simply look up.
                var hasher = new PasswordHasher<User>();
                var seedPassword = initialAdminPassword;
                var wasGenerated = false;

                if (string.IsNullOrWhiteSpace(seedPassword))
                {
                    seedPassword = GenerateRandomPassword();
                    wasGenerated = true;
                }

                admin.PasswordHash = hasher.HashPassword(admin, seedPassword);

                context.Users.Add(admin);
                await context.SaveChangesAsync();

                // Logged only when generated, and only on the run that
                // actually creates the account. This is the single chance to
                // capture it -- only the hash is persisted.
                if (wasGenerated)
                {
                    const string warning =
                        "Seeded administrator {Email} with a GENERATED password: {Password}  " +
                        "Sign in and change it now. It is stored nowhere else.";

                    if (logger is not null)
                        logger.LogWarning(warning, admin.Email, seedPassword);
                    else
                        Console.WriteLine(
                            $"Seeded administrator {admin.Email} with GENERATED password: " +
                            $"{seedPassword}  Sign in and change it now.");
                }
            }
        }

        // ~144 bits of entropy. The suffix guarantees an upper, a lower, a
        // digit and a symbol are present, so the value satisfies any password
        // policy layered on later without weakening the random portion.
        private static string GenerateRandomPassword()
        {
            var bytes = RandomNumberGenerator.GetBytes(18);

            var body = Convert.ToBase64String(bytes)
                .Replace('+', 'K')
                .Replace('/', 'm')
                .TrimEnd('=');

            return body + "!aA1";
        }
    }
}