using System;
using System.Linq;

namespace SchoolInventoryManagement.BLL.Services
{
    // The rules every NEW password has to meet: when a user is added, when
    // an administrator resets one, and when someone changes their own.
    // Signing in never checks them, so an older password that breaks a
    // rule still works until it is next changed.
    //
    // The forms check the same rules (the view models use the pattern and
    // messages below, and Change password shows them as a checklist), but
    // a form can be skipped; the services call Ensure, which can't.
    public static class PasswordPolicy
    {
        public const int MinLength = 8;

        // For [RegularExpression] on the view models. The whole value has
        // to match, so this reads "contains a digit somewhere". 0-9 only,
        // the same digits the browser's check counts.
        public const string HasNumberPattern = @"^.*[0-9].*$";

        public const string TooShortMessage = "Use at least 8 characters.";
        public const string NoNumberMessage = "Include at least one number.";
        public const string SameAsCurrentMessage = "Choose a password different from your current one.";

        public static bool HasNumber(string? password) =>
            password is not null && password.Any(c => c >= '0' && c <= '9');

        public static void Ensure(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < MinLength)
                throw new ArgumentException(TooShortMessage);

            if (!HasNumber(password))
                throw new ArgumentException(NoNumberMessage);
        }
    }
}
