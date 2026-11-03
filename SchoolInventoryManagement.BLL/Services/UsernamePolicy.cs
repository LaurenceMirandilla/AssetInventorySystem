using System;
using System.Text.RegularExpressions;

namespace SchoolInventoryManagement.BLL.Services
{
    // What a username may look like. It is what people type to sign in, so
    // it is kept short and plain: 3 to 50 letters, numbers, dots, dashes
    // or underscores, no spaces. Case doesn't matter: "Admin" and "admin"
    // are the same account, both when signing in and when checking that a
    // new one is free (UserService, AuthService).
    //
    // The Add and Edit user forms check the pattern too; the services call
    // Ensure, which a hand-built post can't skip.
    public static class UsernamePolicy
    {
        public const int MinLength = 3;
        public const int MaxLength = 50;

        public const string Pattern = @"^[A-Za-z0-9._-]{3,50}$";

        public const string Message =
            "Use 3 to 50 letters, numbers, dots, dashes or underscores, with no spaces.";

        public const string TakenMessage = "That username is already taken.";

        private static readonly Regex Valid = new(Pattern, RegexOptions.CultureInvariant);

        // Spaces typed around it are dropped rather than refused.
        public static string Normalize(string? username) => (username ?? "").Trim();

        public static void Ensure(string username)
        {
            if (!Valid.IsMatch(username))
                throw new ArgumentException(Message);
        }
    }
}
