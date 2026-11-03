using Microsoft.Extensions.Configuration;
using SchoolInventoryManagement.BLL.Services;

namespace SchoolInventoryManagement.Web.Helpers
{
    // The password every new account starts with, from appsettings.json:
    //   "Accounts": { "DefaultPassword": "Sabs@2026" }
    // Shown on Add user so the administrator can tell the new person. It
    // only gets them as far as Change password: the account is flagged to
    // choose its own on first sign-in (MustChangePasswordFilter), and the
    // new one has to differ from this.
    public class AccountDefaults
    {
        public const string ClaimType = "must_change_password";

        public AccountDefaults(IConfiguration configuration)
        {
            DefaultPassword = configuration["Accounts:DefaultPassword"]?.Trim();
        }

        public string? DefaultPassword { get; }

        // Missing, or breaking the password rules: Add user refuses and
        // says to set it, rather than creating accounts with a bad one.
        public bool IsUsable =>
            !string.IsNullOrEmpty(DefaultPassword)
            && DefaultPassword.Length >= PasswordPolicy.MinLength
            && PasswordPolicy.HasNumber(DefaultPassword);
    }
}
