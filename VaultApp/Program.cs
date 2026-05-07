using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VaultApp.Data;
using VaultApp.Models;
using VaultApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Database ──────────────────────────────────────────────────────────────────
//builder.Services.AddDbContext<ApplicationDbContext>(options =>
//    options.UseSqlite(builder.Configuration.GetConnectionString("Default")
//        ?? "Data Source=vault.db"));
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// ── Identity ──────────────────────────────────────────────────────────────────
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 8;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

//builder.Services.AddDefaultIdentity<IdentityUser>(options =>
//    {
//        options.Password.RequireNonAlphanumeric = false;
//        options.Password.RequiredLength = 8;
//        options.Lockout.MaxFailedAccessAttempts = 5;
//        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
//        options.User.RequireUniqueEmail = true;
//        options.SignIn.RequireConfirmedAccount = true;
//    })
//    .AddEntityFrameworkStores<ApplicationDbContext>()
//    .AddDefaultTokenProviders();


builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath  = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
});

// ── Session (used to hold the encryption key in memory only) ─────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout        = TimeSpan.FromMinutes(30);
    o.Cookie.HttpOnly    = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

// ── App services ──────────────────────────────────────────────────────────────
builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IVaultService, VaultService>();
builder.Services.AddSingleton<IShareInviteQueue, ShareInviteQueue>();
builder.Services.AddHostedService<ShareInviteWorker>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
