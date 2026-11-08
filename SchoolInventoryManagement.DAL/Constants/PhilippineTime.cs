using System;

namespace SchoolInventoryManagement.DAL.Constants
{
    // The school's clock. The app works in Philippine time (UTC+8) whatever
    // time zone the server it runs on is set to: requests, approvals,
    // returns, overdue checks, notifications and audit logs all use it.
    // The database itself stores UTC; ApplicationDbContext converts between
    // the two with ToUtc and FromUtc as rows are saved and read.
    //
    // How it knows the time: it takes the server's UTC clock, which Windows
    // keeps correct by syncing with internet time servers, and converts it to
    // Philippine time (UTC+8). The Philippines has no daylight saving time,
    // so the difference is always exactly 8 hours.
    public static class PhilippineTime
    {
        public static readonly TimeSpan Offset = TimeSpan.FromHours(8);

        private static readonly TimeZoneInfo Zone = FindZone();

        public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

        public static DateTime Today => Now.Date;

        // Philippine time -> UTC, for saving. (The very first 8 hours of
        // year 1 can't move back, so DateTime.MinValue stays as it is.)
        public static DateTime ToUtc(DateTime philippineTime) =>
            philippineTime.Ticks < Offset.Ticks
                ? philippineTime
                : DateTime.SpecifyKind(philippineTime - Offset, DateTimeKind.Utc);

        // UTC (as stored) -> Philippine time, for using and showing.
        public static DateTime FromUtc(DateTime utc) =>
            utc.Ticks > DateTime.MaxValue.Ticks - Offset.Ticks
                ? utc
                : DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

        // "Asia/Manila" on Linux and on Windows with ICU; "Singapore Standard
        // Time" is Windows' own name for the same UTC+8 zone. If neither is
        // installed, a fixed UTC+8 zone gives the same result.
        private static TimeZoneInfo FindZone()
        {
            foreach (var id in new[] { "Asia/Manila", "Singapore Standard Time" })
            {
                try
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                    if (zone.BaseUtcOffset == Offset)
                        return zone;
                }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("Philippine Time", Offset, "Philippine Time", "Philippine Time");
        }
    }
}
