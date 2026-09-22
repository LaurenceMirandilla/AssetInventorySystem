using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Minimal RFC 4180 CSV writer. Reports get opened in Excel and filed,
    // so correctness around quoting matters more than features here — an
    // unescaped comma in a disposal reason would silently shift every
    // column after it.
    public static class CsvExportHelper
    {
        public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<string?>> rows)
        {
            var sb = new StringBuilder();

            sb.AppendLine(string.Join(",", headers.Select(Escape)));

            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(Escape)));

            // Excel assumes the system codepage unless the file opens with a
            // UTF-8 BOM, which mangles any non-ASCII name or note. Emit one.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                .GetBytes(sb.ToString());
        }

        // A field needs quoting if it contains a comma, a quote, or a line
        // break; inner quotes are doubled.
        private static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var needsQuoting = value.Contains(',')
                || value.Contains('"')
                || value.Contains('\n')
                || value.Contains('\r');

            if (!needsQuoting)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        // Reports are filed by date, so the filename carries one.
        public static string TimestampedFileName(string reportName)
        {
            return $"{reportName}-{DateTime.Now:yyyy-MM-dd}.csv";
        }
    }
}
