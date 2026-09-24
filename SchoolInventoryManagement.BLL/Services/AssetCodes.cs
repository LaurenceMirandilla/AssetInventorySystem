using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SchoolInventoryManagement.BLL.Services
{
    // How asset codes are built: {category prefix}-{number}, numbered from
    // 0001 up to 10000 per prefix -- CHAIR-0001, CHAIR-0002, ... The next
    // number is one past the highest already used with that prefix, so a
    // disposed or deleted unit's number is never handed out again.
    public static class AssetCodes
    {
        public const int MaxNumber = 10000;

        public static string Format(string prefix, int number) =>
            $"{prefix}-{number.ToString("D4", CultureInfo.InvariantCulture)}";

        // The number in "CHAIR-0042" for prefix CHAIR, or null if the code
        // does not follow the pattern (older hand-typed codes, say).
        public static int? NumberOf(string code, string prefix)
        {
            var start = prefix + "-";
            if (!code.StartsWith(start, System.StringComparison.OrdinalIgnoreCase))
                return null;

            var digits = code.Substring(start.Length);
            if (digits.Length == 0 || !digits.All(char.IsDigit))
                return null;

            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        // Next free number for the prefix, given every code that starts
        // with it.
        public static int NextNumber(string prefix, IEnumerable<string> codes)
        {
            var highest = codes
                .Select(c => NumberOf(c, prefix))
                .Where(n => n is not null)
                .Select(n => n!.Value)
                .DefaultIfEmpty(0)
                .Max();

            return highest + 1;
        }
    }
}