using System;

namespace SchoolInventoryManagement.BLL.Services
{
    // Thrown when a save fails because another user modified the same
    // record first (RowVersion mismatch). Wraps EF Core's
    // DbUpdateConcurrencyException so callers outside the BLL never need
    // to know EF Core is involved at all.
    public class ConcurrencyConflictException : Exception
    {
        public ConcurrencyConflictException(string message) : base(message) { }
    }
}