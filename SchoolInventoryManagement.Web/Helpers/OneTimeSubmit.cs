using System;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace SchoolInventoryManagement.Web.Helpers
{
    // Stops a form that creates something from creating it twice: a double
    // click before site.js loads, a network retry, or Back and Submit again
    // on the same page.
    //
    // The form carries a one-time ticket (@Html.FormTicket()), a fresh
    // random value each time the page is drawn. The first post with a
    // ticket does the work. A second post with the same ticket and the same
    // answers never reaches the action:
    //   - if the first one is still saving, it waits for it;
    //   - if the first one saved, it goes to the same page the first one
    //     went to, with the same message, so the user sees one record;
    //   - if the first one failed (a validation message, an error), the
    //     ticket is let go and this post runs normally.
    // Changing the answers on the same page (Back, edit, Submit) counts as a
    // new submission, so a second, different record can still be made.
    //
    // A post without a ticket (an old page still open from before this
    // change) runs normally.
    //
    // Tickets live in this server's memory for two hours. That is enough for
    // one server; a web farm would need a shared cache instead.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class OneTimeSubmitAttribute : Attribute, IAsyncActionFilter
    {
        public const string FieldName = "__FormTicket";

        private static readonly object Gate = new();
        private static readonly TimeSpan Keep = TimeSpan.FromHours(2);
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(60);

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var http = context.HttpContext;
            if (!HttpMethods.IsPost(http.Request.Method) || !http.Request.HasFormContentType)
            {
                await next();
                return;
            }

            var form = await http.Request.ReadFormAsync();
            var ticket = form[FieldName].ToString();
            if (ticket.Length == 0 || ticket.Length > 64)
            {
                await next();
                return;
            }

            var cache = http.RequestServices.GetRequiredService<IMemoryCache>();
            var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous";
            var key = $"form-ticket:{user}:{ticket}";
            var print = Fingerprint(form);

            while (true)
            {
                Submission? mine = null;
                Submission? earlier;
                lock (Gate)
                {
                    earlier = cache.Get<Submission>(key);
                    if (earlier is null || earlier.Fingerprint != print)
                    {
                        mine = new Submission(print);
                        cache.Set(key, mine, Keep);
                    }
                }

                if (mine is not null)
                {
                    await RunAsync(context, next, cache, key, mine);
                    return;
                }

                Outcome? outcome;
                try
                {
                    outcome = await earlier!.Done.Task.WaitAsync(MaxWait);
                }
                catch (TimeoutException)
                {
                    // The first one is taking very long. Don't save a second
                    // copy; send them back to the empty form instead.
                    TempDataOf(http)["StatusMessage"] =
                        "Your first submit is still being saved. Check the list in a minute before sending it again.";
                    context.Result = new RedirectResult(http.Request.PathBase + http.Request.Path);
                    return;
                }

                if (outcome is not null)
                {
                    if (outcome.Message is not null)
                        TempDataOf(http)["StatusMessage"] = outcome.Message;
                    context.Result = new RedirectResult(outcome.Url);
                    return;
                }

                // The first one failed and let the ticket go. Try again; this
                // post will usually take the ticket and run.
            }
        }

        private static async Task RunAsync(ActionExecutingContext context, ActionExecutionDelegate next,
            IMemoryCache cache, string key, Submission mine)
        {
            Outcome? outcome = null;
            try
            {
                var executed = await next();
                if (executed.Exception is null || executed.ExceptionHandled)
                {
                    var url = RedirectUrl(executed);
                    if (url is not null)
                        // Peek, so the message still shows on the next page.
                        outcome = new Outcome(url, TempDataOf(context.HttpContext).Peek("StatusMessage") as string);
                }
            }
            finally
            {
                if (outcome is null)
                {
                    // Nothing was saved (the form came back with a message,
                    // or it failed), so the same ticket may be used again.
                    lock (Gate)
                    {
                        if (ReferenceEquals(cache.Get<Submission>(key), mine))
                            cache.Remove(key);
                    }
                }
                mine.Done.TrySetResult(outcome);
            }
        }

        // Every create action ends in a redirect when it saves, and shows
        // the form again when it doesn't.
        private static string? RedirectUrl(ActionExecutedContext executed)
        {
            switch (executed.Result)
            {
                case RedirectToActionResult r:
                    var urls = executed.HttpContext.RequestServices
                        .GetRequiredService<IUrlHelperFactory>().GetUrlHelper(executed);
                    var url = urls.Action(r.ActionName, r.ControllerName, r.RouteValues);
                    return url is null ? null : url + (r.Fragment is null ? "" : "#" + r.Fragment);
                case RedirectResult r:
                    return r.Url;
                case LocalRedirectResult r:
                    return r.Url;
                default:
                    return null;
            }
        }

        // The posted answers, without the per-page security values, so the
        // same form posted twice gives the same fingerprint.
        private static string Fingerprint(IFormCollection form)
        {
            var text = new StringBuilder();
            foreach (var field in form.Keys.Where(k => k != FieldName && k != "__RequestVerificationToken")
                                           .OrderBy(k => k, StringComparer.Ordinal))
            {
                text.Append(field).Append('=');
                foreach (var value in form[field])
                    text.Append(value).Append('\u001f');
                text.Append('\u001e');
            }
            foreach (var file in form.Files.OrderBy(f => f.Name, StringComparer.Ordinal))
                text.Append(file.Name).Append(':').Append(file.FileName).Append(':').Append(file.Length).Append('\u001e');

            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }

        private static ITempDataDictionary TempDataOf(HttpContext http) =>
            http.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(http);

        private sealed class Submission
        {
            public Submission(string fingerprint) => Fingerprint = fingerprint;
            public string Fingerprint { get; }
            public TaskCompletionSource<Outcome?> Done { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed record Outcome(string Url, string? Message);
    }

    public static class FormTicketHtmlExtensions
    {
        // Put inside a form whose POST action has [OneTimeSubmit]. A new
        // value every time the page is drawn, so a form shown again after a
        // validation message gets a fresh one.
        public static IHtmlContent FormTicket(this IHtmlHelper html) =>
            new HtmlString($"<input type=\"hidden\" name=\"{OneTimeSubmitAttribute.FieldName}\" value=\"{Guid.NewGuid():N}\" />");
    }
}
