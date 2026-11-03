using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using SchoolInventoryManagement.DAL.Constants;

namespace SchoolInventoryManagement.Web.Helpers
{
    // The sentence on the Access denied page: "Recording returns is for
    // Asset Officers and Administrators." Built from the page the user was
    // turned away from (the ReturnUrl the sign-in cookie adds).
    //
    // Who a page is for is read from its own [Authorize(Roles = ...)]
    // attributes, so it can't drift from the real rule. Only the page's
    // name is written out here; a page missing from the list reads "This
    // page".
    public class AccessDeniedText
    {
        private readonly IActionDescriptorCollectionProvider _actions;

        public AccessDeniedText(IActionDescriptorCollectionProvider actions)
        {
            _actions = actions;
        }

        // "Controller/Action", or "Controller/*" for the whole controller.
        private static readonly Dictionary<string, string> PageNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AssetAssignments/Return"] = "Returning assets",
            ["AssetAssignments/*"] = "Assigning assets",
            ["AssetMovements/*"] = "Transferring assets",
            ["AssetRequests/Pending"] = "The Approvals page",
            ["AssetRequests/History"] = "Request History",
            ["AssetRequests/RecordReturn"] = "Recording returns",
            ["AssetRequests/Reject"] = "Rejecting requests",
            ["AssetRequests/Approve"] = "Approving requests",
            ["AssetRequests/ApproveBorrow"] = "Approving requests",
            ["AssetRequests/MarkAssigned"] = "Handing over requested assets",
            ["Assets/Create"] = "Registering assets",
            ["Assets/BulkCreate"] = "Registering assets",
            ["Assets/Edit"] = "Editing assets",
            ["Assets/Dispose"] = "Disposing of assets",
            ["Assets/History"] = "Asset history",
            ["Branches/*"] = "Managing branches",
            ["Categories/*"] = "Managing categories",
            ["Departments/*"] = "Managing departments",
            ["Locations/*"] = "Managing locations",
            ["Models/*"] = "Managing models",
            ["NewItemRequests/All"] = "The list of all new-item requests",
            ["NewItemRequests/Pending"] = "New-item approvals",
            ["Reports/*"] = "Reports",
            ["Users/ResetPassword"] = "Resetting passwords",
            ["Users/*"] = "Managing users",
        };

        // Plural names, in the order they're listed: the people who
        // request first, the people who run things last.
        private static readonly (string Role, string Plural)[] RoleOrder =
        {
            (RoleNames.Teacher, "Teachers"),
            (RoleNames.Staff, "Staff"),
            (RoleNames.DepartmentHead, "Department Heads"),
            (RoleNames.AssetOfficer, "Asset Officers"),
            (RoleNames.Principal, "Principals"),
            (RoleNames.Administrator, "Administrators"),
        };

        // Null when the page can't be worked out (no ReturnUrl, or one
        // that isn't a page here); the view then says it generally.
        public string? For(string? returnUrl)
        {
            if (string.IsNullOrEmpty(returnUrl) || !returnUrl.StartsWith('/') || returnUrl.StartsWith("//"))
                return null;

            var path = returnUrl.Split('?', '#')[0];
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var controller = parts.Length > 0 ? parts[0] : "Home";
            var action = parts.Length > 1 ? parts[1] : "Index";

            var candidates = _actions.ActionDescriptors.Items
                .OfType<ControllerActionDescriptor>()
                .Where(d => d.ControllerName.Equals(controller, StringComparison.OrdinalIgnoreCase)
                            && d.ActionName.Equals(action, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
                return null;

            // The GET version when there is one: a page is what was opened.
            var descriptor = candidates.FirstOrDefault(IsGet) ?? candidates[0];

            // Each [Authorize(Roles)] (controller and action) must be met,
            // so the page is for the roles every one of them allows.
            HashSet<string>? allowed = null;
            foreach (var rule in descriptor.EndpointMetadata.OfType<IAuthorizeData>())
            {
                if (string.IsNullOrWhiteSpace(rule.Roles))
                    continue;
                var roles = rule.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (allowed is null)
                    allowed = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
                else
                    allowed.IntersectWith(roles);
            }
            if (allowed is null || allowed.Count == 0)
                return null;

            var plurals = RoleOrder.Where(r => allowed.Contains(r.Role)).Select(r => r.Plural).ToList();
            if (plurals.Count == 0)
                return null;

            var name = PageNames.GetValueOrDefault($"{descriptor.ControllerName}/{descriptor.ActionName}")
                       ?? PageNames.GetValueOrDefault($"{descriptor.ControllerName}/*")
                       ?? "This page";

            return $"{name} is for {JoinAnd(plurals)}.";
        }

        private static bool IsGet(ControllerActionDescriptor d)
        {
            var methods = d.ActionConstraints?.OfType<HttpMethodActionConstraint>().SelectMany(c => c.HttpMethods).ToList();
            return methods is null || methods.Count == 0 || methods.Contains("GET", StringComparer.OrdinalIgnoreCase);
        }

        private static string JoinAnd(IReadOnlyList<string> items) => items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
        };
    }
}
