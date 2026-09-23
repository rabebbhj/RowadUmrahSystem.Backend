using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using RowadUmrahSystem.Web.Data;
using RowadUmrahSystem.Web.Hubs;
using RowadUmrahSystem.Web.Models;
using RowadUmrahSystem.Web.Services;
using RowadUmrahSystem.Web.Services.PdfReports;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
})
.AddRoles<IdentityRole>()
.AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";

    options.Events.OnValidatePrincipal = async context =>
    {
        var userManager = context.HttpContext.RequestServices
            .GetRequiredService<UserManager<ApplicationUser>>();

        var principal = context.Principal;
        if (principal == null)
        {
            return;
        }

        var user = await userManager.GetUserAsync(principal);

        if (user != null && !user.IsActive)
        {
            context.RejectPrincipal();

            await context.HttpContext.SignOutAsync(
                IdentityConstants.ApplicationScheme);
        }
    };
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.AddSignalR();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<PassportOcrService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IPdfReportService, PdfReportService>();

QuestPDF.Settings.License = LicenseType.Community;

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();

app.UseRouting();

app.UseCors("FrontendDev");

app.UseAuthentication();
app.UseAuthorization();

string[] adminSpaRoutes =
{
    "/login",
    "/admin",
    "/travelers",
    "/trips",
    "/users",
    "/accounting",
    "/documents",
    "/audit-logs",
    "/auditlogs",
    "/notifications",
    "/settings",
    "/financial-reports",
    "/accounts",
    "/bank-accounts",
    "/customers",
    "/invoices",
    "/expenses",
    "/journal-entries",
    "/receipt-vouchers",
    "/payment-vouchers"
};

string[] backendActionPrefixes =
{
    "/api",
    "/identity",
    "/notifications/mynotifications",
    "/notifications/open",
    "/notifications/markallasread",
    "/notifications/markasread",
    "/notifications/delete",
    "/notifications/deleteall",
    "/travelerdocuments",
    "/travelers/export",
    "/travelers/blockedpdf",
    "/travelers/deletedpdf",
    "/travelers/exportdeleted",
    "/travelers/exportblocked",
    "/travelers/print",
    "/trips/export",
    "/invoices/export",
    "/accounts/export",
    "/journalentries/export"
};

static bool IsAdminSpaPageRequest(HttpRequest request, string[] spaRoutes, string[] backendPrefixes)
{
    if (!HttpMethods.IsGet(request.Method))
    {
        return false;
    }

    var path = request.Path.Value?.TrimEnd('/').ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(path))
    {
        return false;
    }

    if (Path.HasExtension(path))
    {
        return false;
    }

    if (backendPrefixes.Any(prefix => path == prefix || path.StartsWith($"{prefix}/")))
    {
        return false;
    }

    return spaRoutes.Any(route => path == route || path.StartsWith($"{route}/"));
}

app.Use(async (context, next) =>
{
    if (!IsAdminSpaPageRequest(context.Request, adminSpaRoutes, backendActionPrefixes))
    {
        await next();
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "app", "index.html"));
});

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

app.MapHub<DashboardHub>("/dashboardHub");

foreach (var route in adminSpaRoutes)
{
    app.MapFallbackToFile(route, "app/index.html");
    app.MapFallbackToFile($"{route}/{{*path:nonfile}}", "app/index.html");
}

using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    string[] roles = { "Admin", "Employee" };

    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }   
    }

    var adminEmail = "admin@rowad.local";
    var adminPassword = "Admin@12345";

    var adminUser = await userManager.FindByEmailAsync(adminEmail);

    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true,
            FullName = "System Admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);

        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }
    else
    {
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(adminUser);
        var resetResult = await userManager.ResetPasswordAsync(adminUser, resetToken, "Admin@12345");

        if (!resetResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to reset password for {adminEmail}: " +
                string.Join(", ", resetResult.Errors.Select(error => error.Description)));
        }

        if (!await userManager.IsInRoleAsync(adminUser, "Admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "Admin");
        }
    }

    var travelerEmail = "traveler@rowad.local";
    var travelerPassword = "Traveler@12345";

    var travelerUser = await userManager.FindByEmailAsync(travelerEmail);

    if (travelerUser == null)
    {
        travelerUser = new ApplicationUser
        {
            UserName = travelerEmail,
            Email = travelerEmail,
            EmailConfirmed = true,
            FullName = "Test Traveler",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(travelerUser, travelerPassword);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create test traveler {travelerEmail}: " +
                string.Join(", ", result.Errors.Select(error => error.Description)));
        }
    }
    else
    {
        var resetToken = await userManager.GeneratePasswordResetTokenAsync(travelerUser);
        var resetResult = await userManager.ResetPasswordAsync(travelerUser, resetToken, travelerPassword);

        if (!resetResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to reset password for {travelerEmail}: " +
                string.Join(", ", resetResult.Errors.Select(error => error.Description)));
        }

        travelerUser.EmailConfirmed = true;
        travelerUser.IsActive = true;
        await userManager.UpdateAsync(travelerUser);
    }
}

app.Run();
