using Microsoft.AspNetCore.Authentication.Cookies;
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
    options.AddInterceptors(serviceProvider.GetRequiredService<AuditSaveChangesInterceptor>());
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddScoped<IAuthService, AuthService>();
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
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditSaveChangesInterceptor>();


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
app.UseMiddleware<SchoolInventoryManagement.Web.Middleware.ExceptionHandlingMiddleware>();

// Turns a bare status code -- most often the NotFound() an MVC action
// returns for an id that no longer exists -- into a real page in the app's
// shell. Without it the browser shows its own blank error, which looks like
// the site is broken rather than like the record is gone. Re-execute rather
// than redirect, so the response keeps the original status code and the
// address bar keeps the URL the user actually asked for.
app.UseStatusCodePagesWithReExecute("/Home/Error/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
