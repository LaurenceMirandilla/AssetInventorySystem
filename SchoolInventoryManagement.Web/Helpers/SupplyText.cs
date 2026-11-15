using System;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Wording for supplies. Units are typed by the user ("box", "ream"),
    // so the plural is worked out here: 1 box, 6 boxes, 42 reams.
    public static class SupplyText
    {
        public static string Unit(int quantity, string unit)
        {
            unit = (unit ?? "").Trim();
            if (quantity == 1 || unit.Length == 0)
                return unit;

            var lower = unit.ToLowerInvariant();
            if (lower.EndsWith("s", StringComparison.Ordinal) && !lower.EndsWith("ss", StringComparison.Ordinal))
                return unit; // already plural: "pcs", "reams"
            if (lower.EndsWith("x", StringComparison.Ordinal) || lower.EndsWith("ch", StringComparison.Ordinal) ||
                lower.EndsWith("sh", StringComparison.Ordinal) || lower.EndsWith("ss", StringComparison.Ordinal))
                return unit + "es";
            return unit + "s";
        }
    }
}
