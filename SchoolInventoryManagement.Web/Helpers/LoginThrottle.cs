using System;
using Microsoft.Extensions.Caching.Memory;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Slows down password guessing. Every 5th wrong password for a username
    // (5th, 10th, 15th, ...) locks that username out of signing in for 30
    // seconds. A correct sign-in clears the count. Attempts made while
    // locked out are refused without checking the password and do not
    // count.
    //
    // Kept in memory, keyed by username (any case): nothing to store in the database,
    // and an app restart simply starts everyone fresh. A count with no
    // new attempts for 15 minutes is forgotten.
    public class LoginThrottle
    {
        public const int AttemptsPerLockout = 5;
        public static readonly TimeSpan LockoutLength = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(15);

        private readonly IMemoryCache _cache;

        public LoginThrottle(IMemoryCache cache)
        {
            _cache = cache;
        }

        private sealed class Entry
        {
            public int Failures;
            public DateTimeOffset LockedUntil;
        }

        private static string Key(string username) =>
            "login-failures:" + (username ?? "").Trim().ToUpperInvariant();

        // Time left on a lockout, or null when the username may try now.
        public TimeSpan? LockoutRemaining(string username)
        {
            if (!_cache.TryGetValue(Key(username), out Entry? entry) || entry is null)
                return null;

            lock (entry)
            {
                var left = entry.LockedUntil - DateTimeOffset.UtcNow;
                return left > TimeSpan.Zero ? left : null;
            }
        }

        // Counts a wrong password. Returns the lockout it started (every
        // 5th failure), or null with how many tries are left before one.
        public (TimeSpan? Lockout, int TriesLeft) RecordFailure(string username)
        {
            var entry = _cache.GetOrCreate(Key(username), e =>
            {
                e.SlidingExpiration = ForgetAfter;
                return new Entry();
            })!;

            lock (entry)
            {
                entry.Failures++;
                if (entry.Failures % AttemptsPerLockout == 0)
                {
                    entry.LockedUntil = DateTimeOffset.UtcNow + LockoutLength;
                    return (LockoutLength, AttemptsPerLockout);
                }

                return (null, AttemptsPerLockout - entry.Failures % AttemptsPerLockout);
            }
        }

        public void Reset(string username) => _cache.Remove(Key(username));
    }
}
