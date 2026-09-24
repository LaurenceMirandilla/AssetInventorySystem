using System.Globalization;
using SchoolInventoryManagement.DAL.Entities.Enums;
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
        // Money is always pesos with centavos: ₱608,180.00. Formatted by
        // hand rather than with "C", which follows the server's culture and
        // printed dollars. Commas for thousands, a point for cents.
        public static string Peso(decimal value) =>
            "₱" + value.ToString("#,##0.00", CultureInfo.InvariantCulture);

        public static string Peso(decimal? value) =>
            value.HasValue ? Peso(value.Value) : "—";

        // A new-item request's stage, as the status pill shows it.
        public static string Stage(NewItemStatus status) => status switch
        {
            NewItemStatus.AwaitingDeptHead => "Awaiting department head",
            NewItemStatus.AwaitingBudget => "Awaiting budget check",
            NewItemStatus.Procuring => "Procuring item",
            NewItemStatus.Arrived => "Item arrived",
            _ => status.ToString()
        };

        // What a step in a new-item request's history did: the stage it
        // moved the request into, said as the action that got it there.
        public static string StepAction(NewItemStatus status) => status switch
        {
            NewItemStatus.AwaitingDeptHead => "Submitted",
            NewItemStatus.AwaitingBudget => "Approved by department head",
            NewItemStatus.Procuring => "Budget approved, procuring item",
            NewItemStatus.Arrived => "Marked as arrived",
            NewItemStatus.Rejected => "Rejected",
            _ => status.ToString()
        };

        // The button that moves a request on from its current stage.
        public static string AdvanceButton(NewItemStatus status) => status switch
        {
            NewItemStatus.AwaitingDeptHead => "Approve",
            NewItemStatus.AwaitingBudget => "Approve (budget available)",
            NewItemStatus.Procuring => "Next step: Item arrived",
            _ => ""
        };

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
