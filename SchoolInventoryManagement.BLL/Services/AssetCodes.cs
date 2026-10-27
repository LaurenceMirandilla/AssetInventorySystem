using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SchoolInventoryManagement.BLL.Services
{
    // Two identifiers per asset:
    //
    //   Asset code  {category}-{number}, e.g. CHR-0001. Given when the
    //               asset is registered and never changes. The number
    //               counts per category across the whole school, 0001 up to
    //               9999. The next number is one past the highest already
    //               used, so a disposed or deleted unit's number is never
    //               handed out again.
    //
    //   Barcode     {branch}-{location}-{asset code}, e.g. M-LIB-CHR-0001.
    //               Built when the barcode is created, from where the asset
    //               is at that moment (XXX with no location), and stored as
    //               printed -- moving the asset later does not change it.
    //               Unique because the asset code inside it is.
    public static class AssetCodes
    {
        public const int MaxNumber = 9999;

        // Location part of a barcode for an asset with no location.
        public const string NoLocation = "XXX";

        public static string Format(string categoryCode, int number) =>
            $"{categoryCode}-{number.ToString("D4", CultureInfo.InvariantCulture)}";

        public static string Barcode(string branchCode, string? locationCode, string assetCode) =>
            $"{branchCode}-{(string.IsNullOrEmpty(locationCode) ? NoLocation : locationCode)}-{assetCode}";

        // The number in "CHR-0042" for category CHR, or null if the code
        // does not follow the pattern.
        public static int? NumberOf(string code, string categoryCode)
        {
            var start = categoryCode + "-";
            if (!code.StartsWith(start, System.StringComparison.OrdinalIgnoreCase))
                return null;

            var digits = code.Substring(start.Length);
            if (digits.Length == 0 || !digits.All(char.IsAsciiDigit))
                return null;

            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        // Next free number for the category, given every code that starts
        // with it.
        public static int NextNumber(string categoryCode, IEnumerable<string> codes)
        {
            var highest = codes
                .Select(c => NumberOf(c, categoryCode))
                .Where(n => n is not null)
                .Select(n => n!.Value)
                .DefaultIfEmpty(0)
                .Max();

            return highest + 1;
        }
    }
}
