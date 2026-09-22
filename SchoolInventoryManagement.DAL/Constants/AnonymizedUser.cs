using System;

namespace SchoolInventoryManagement.DAL.Constants
{
    // The shape of an anonymized account, in one place because two layers
    // have to agree on it: UserService writes it, and
    // AuditSaveChangesInterceptor has to recognise it so the audit trail
    // does not preserve the very identity the anonymization just erased.
    public static class AnonymizedUser
    {
        public const string FirstName = "Anonymized";
        public const string LastName = "User";

        private const string EmailPrefix = "anonymized-";
        private const string EmailDomain = "@removed.invalid";

        // Users.Email carries a UNIQUE constraint, so the placeholder has to
        // differ per account -- a single shared value would collide the
        // second time anyone is anonymized. UserID is already unique and is
        // not personal information, so it is the natural discriminator.
        // .invalid is reserved by RFC 2606 and can never be a real domain,
        // so this address can never accidentally reach a mailbox.
        public static string EmailFor(int userId) => $"{EmailPrefix}{userId}{EmailDomain}";

        public static bool IsAnonymizedEmail(string? email) =>
            email is not null
            && email.StartsWith(EmailPrefix, StringComparison.Ordinal)
            && email.EndsWith(EmailDomain, StringComparison.Ordinal);
    }
}
