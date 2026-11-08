using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.BLL.Interfaces;
using SchoolInventoryManagement.BLL.Services;
using SchoolInventoryManagement.DAL.Context;
using SchoolInventoryManagement.DAL.Interceptors;
using SchoolInventoryManagement.DAL.Seed;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>((serviceProvider, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    // Dates first, then the audit trail, so the history records real dates.
    options.AddInterceptors(
        new PhilippineTimeInterceptor(),
        serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // HTTPS only: the browser never sends the sign-in cookie over plain
        // HTTP, and page scripts can't read it.
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });

// ---- HTTPS only ----------------------------------------------------------
// The form-protection and TempData cookies are HTTPS-only too.
builder.Services.AddAntiforgery(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);
builder.Services.Configure<Microsoft.AspNetCore.Mvc.CookieTempDataProviderOptions>(options => options.Cookie.SecurePolicy = CookieSecurePolicy.Always);

// HTTP is answered with a redirect to HTTPS: permanent (308) on the live
// site, temporary while developing so the browser doesn't remember it.
builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = builder.Environment.IsDevelopment()
        ? StatusCodes.Status307TemporaryRedirect
        : StatusCodes.Status308PermanentRedirect;
    // Set "HttpsPort" in appsettings (or ASPNETCORE_HTTPS_PORT) if the
    // site's HTTPS port isn't 443 and can't be detected.
});

// HSTS: once a browser has seen the site over HTTPS, it refuses plain
// HTTP for it for a year, even if someone types http:// or follows an old
// link. Live site only (see UseHsts below); never on localhost.
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

// Behind a reverse proxy (nginx, a load balancer) the app itself may be
// reached over HTTP while the visitor used HTTPS; the proxy says so in
// X-Forwarded-Proto. Only proxies on this machine are trusted by default.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<SchoolInventoryManagement.Web.Helpers.AuditText>();
// Counts and delete blockers on the catalog / organisation pages.
builder.Services.AddScoped<SchoolInventoryManagement.Web.Helpers.LookupUsage>();
// What one person holds and has open (Users pages).
builder.Services.AddScoped<SchoolInventoryManagement.Web.Helpers.UserFacts>();
// "Recording returns is for ..." on the Access denied page.
builder.Services.AddSingleton<SchoolInventoryManagement.Web.Helpers.AccessDeniedText>();
builder.Services.AddScoped<IAssetService, AssetService>();
builder.Services.AddScoped<IAssetRequestService, AssetRequestService>();
builder.Services.AddScoped<IAssetAssignmentService, AssetAssignmentService>();
builder.Services.AddScoped<IRequestFulfillmentService, RequestFulfillmentService>();
builder.Services.AddScoped<IAssetMovementService, AssetMovementService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IDisposalService, DisposalService>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IModelService, ModelService>();
builder.Services.AddScoped<IBranchService, BranchService>();
builder.Services.AddScoped<IDepartmentService, DepartmentService>();
builder.Services.AddScoped<ILocationService, LocationService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
// Someone on a starting password can only change it (Helpers/MustChangePasswordFilter).
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add<SchoolInventoryManagement.Web.Helpers.MustChangePasswordFilter>());
// The password new accounts start with (appsettings: Accounts:DefaultPassword).
builder.Services.AddSingleton<SchoolInventoryManagement.Web.Helpers.AccountDefaults>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();
builder.Services.AddScoped<INewItemRequestService, NewItemRequestService>();
// Wrong-password lockouts (Helpers/LoginThrottle), kept in memory.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<SchoolInventoryManagement.Web.Helpers.LoginThrottle>();


var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Seed:AdminPassword sets the initial administrator password on a fresh
    // database. Leave it unset and DbSeeder generates a random one and logs
    // it once. Set it via user-secrets or an environment variable
    // (Seed__AdminPassword) -- never commit a real value to appsettings.json.
    var seedLogger = scope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("DbSeeder");

    await DbSeeder.SeedAsync(
        db,
        builder.Configuration["Seed:AdminPassword"],
        seedLogger);
}
app.UseForwardedHeaders();

app.UseMiddleware<SchoolInventoryManagement.Web.Middleware.ExceptionHandlingMiddleware>();

// Turns a bare status code -- most often the NotFound() an MVC action
// returns for an id that no longer exists -- into a real page in the app's
// shell. Without it the browser shows its own blank error, which looks like
// the site is broken rather than like the record is gone. Re-execute rather
// than redirect, so the response keeps the original status code and the
// address bar keeps the URL the user actually asked for.
app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

if (!app.Environment.IsDevelopment())
    app.UseHsts();
app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
