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

        // A value, or -- when there is none -- a word saying so in grey
        // italics ("Not yet", "None given", "On the shelf"), never a bare
        // dash. The word is passed in because what "missing" means depends
        // on the field.
        public static Microsoft.AspNetCore.Html.IHtmlContent Or(string? value, string whenMissing) =>
            string.IsNullOrWhiteSpace(value)
                ? new Microsoft.AspNetCore.Html.HtmlString(
                    "<span class=\"is-empty\">" + System.Net.WebUtility.HtmlEncode(whenMissing) + "</span>")
                : new Microsoft.AspNetCore.Html.HtmlString(System.Net.WebUtility.HtmlEncode(value));

        // A supply request's stage, as the status pill shows it.
        public static string SupplyStage(SupplyRequestStatus status) => status switch
        {
            SupplyRequestStatus.Pending => "Waiting for approval",
            SupplyRequestStatus.Approved => "Approved, to be released",
            SupplyRequestStatus.Released => "Out for delivery",
            SupplyRequestStatus.Delivered => "Delivered",
            _ => status.ToString()
        };

        public static string SupplyStagePill(SupplyRequestStatus status) => status switch
        {
            SupplyRequestStatus.Pending => "st-low",
            SupplyRequestStatus.Approved => "st-intransit",
            SupplyRequestStatus.Released => "st-intransit",
            SupplyRequestStatus.Delivered => "st-available",
            SupplyRequestStatus.Rejected => "st-overdue",
            _ => "st-undermaintenance"
        };

        // A new-item request's stage, as the status pill shows it.
        public static string Stage(NewItemStatus status) => status switch
        {
            // No longer a stage of its own: read as waiting for the Principal.
            NewItemStatus.AwaitingDeptHead => "Awaiting Principal",
            NewItemStatus.AwaitingBudget => "Awaiting Principal",
            NewItemStatus.Procuring => "Item procurement",
            NewItemStatus.AwaitingArrival => "Waiting for item arrival",
            NewItemStatus.Arrived => "Arrived, to be delivered",
            NewItemStatus.Delivered => "Delivered",
            _ => status.ToString()
        };

        // What a step in a new-item request's history did: the stage it
        // moved the request into, said as the action that got it there.
        public static string StepAction(NewItemStatus status) => status switch
        {
            NewItemStatus.AwaitingDeptHead => "Submitted",
            // Old tickets only, from when a department head came first.
            NewItemStatus.AwaitingBudget => "Approved by department head",
            NewItemStatus.Procuring => "Approved by Principal, sent for procurement",
            NewItemStatus.AwaitingArrival => "Item procured",
            NewItemStatus.Arrived => "Arrival confirmed",
            NewItemStatus.Delivered => "Delivered",
            NewItemStatus.DeliveryFailed => "Could not be delivered",
            NewItemStatus.Rejected => "Rejected",
            _ => status.ToString()
        };

        // The button that moves a request on from its current stage.
        public static string AdvanceButton(NewItemStatus status) => status switch
        {
            NewItemStatus.AwaitingDeptHead => "Approve",
            NewItemStatus.AwaitingBudget => "Approve",
            NewItemStatus.Procuring => "Item procured",
            NewItemStatus.AwaitingArrival => "Confirm arrival",
            NewItemStatus.Arrived => "Delivered",
            _ => ""
        };

        // "In transit" used to cover two different things: units set aside at
        // the pickup point for a borrow, and units being delivered for a
        // transfer. Each now says which. Without the request type (a unit on
        // its own), both are named.
        public static string RequestStatusLabel(RequestStatus status, RequestType? type) =>
            status == RequestStatus.InTransit ? InTransitLabel(type) : Humanize(status.ToString());

        public static string AssetStatusLabel(AssetStatus status, RequestType? type = null) =>
            status == AssetStatus.InTransit ? InTransitLabel(type) : Humanize(status.ToString());

        public static string InTransitLabel(RequestType? type) => type switch
        {
            RequestType.Borrow => "Ready for pickup",
            RequestType.Transfer => "On its way",
            _ => "Ready for pickup / on its way"
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
