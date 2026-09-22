using System.Text;

namespace SchoolInventoryManagement.Web.Helpers
{
    public static class DisplayText
    {
        // Enum names are single words by necessity; the screens they appear on
        // are not. "UnderMaintenance" becomes "Under maintenance", matching
        // how the mockups label every pill. Only the first word keeps its
        // capital, so it reads as a label rather than a Title.
        //
        // The CSS class still derives from the raw name (status-undermaintenance),
        // so this is display only and cannot break the pill colours.
        public static string Humanize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var result = new StringBuilder(value.Length + 4);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (i > 0 && char.IsUpper(c))
                {
                    result.Append(' ');
                    result.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString();
        }
    }
}
