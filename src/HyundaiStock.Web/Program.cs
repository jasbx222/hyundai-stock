using System.Text;
using System.Threading.RateLimiting;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using HyundaiStock.Web.Api;
using HyundaiStock.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ---------- Database (SQL Server is the target; Sqlite only for local verification)
var provider = cfg["Database:Provider"] ?? "SqlServer";
var cs = cfg.GetConnectionString("Default");
if (string.IsNullOrWhiteSpace(cs)) throw new InvalidOperationException("ConnectionStrings:Default is not configured (use appsettings.Development.json, user-secrets or the ConnectionStrings__Default env var).");
builder.Services.AddDbContext<AppDbContext>(o =>
{
    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        var sqlite = new SqliteConnectionStringBuilder(cs);
        // Do not let a busy local database consume the relay's entire request budget.
        if (!sqlite.ShouldSerialize("Default Timeout")) sqlite.DefaultTimeout = 5;
        o.UseSqlite(sqlite.ToString());
    }
    else o.UseSqlServer(cs, s => s.UseCompatibilityLevel(120).MigrationsAssembly(typeof(AppDbContext).Assembly.FullName).EnableRetryOnFailure(3));
});

// ---------- Services
builder.Services.Configure<StorageOptions>(cfg.GetSection("Storage"));
builder.Services.Configure<SeedOptions>(cfg.GetSection("Seed"));
builder.Services.Configure<JwtOptions>(cfg.GetSection("Jwt"));
builder.Services.AddSingleton<IPasswordService, PasswordService>();
builder.Services.AddSingleton<ExcelImportService>();
builder.Services.AddScoped<SessionLookup>();
builder.Services.AddScoped<BranchService>();
builder.Services.AddScoped<WarehouseService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PartService>();
builder.Services.AddScoped<QuantityRequestService>();
builder.Services.AddScoped<VinService>();
builder.Services.AddScoped<VinRequestService>();
builder.Services.AddScoped<SessionService>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<MobileService>();
builder.Services.AddScoped<NewPartRequestService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AdminScopeFilter>();

// Only scheme forwarding is needed: the relay already preserves Host. Never trust
// arbitrary internet proxies or X-Forwarded-Host. Kestrel must remain loopback-only.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
});

// ---------- Auth: cookie for the dashboard, JWT bearer for the mobile API
var jwt = cfg.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.Key) || Encoding.UTF8.GetByteCount(jwt.Key) < 32)
    throw new InvalidOperationException("Jwt:Key must be configured (>= 32 bytes) outside appsettings.json (appsettings.Development.json, user-secrets or Jwt__Key env var).");

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/";
        o.Cookie.Name = "hyst.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(12);
        o.SlidingExpiration = true;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwt.Issuer,
            ValidateAudience = true, ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1)
        };
        o.Events = new JwtBearerEvents
        {
            OnTokenValidated = async ctx =>
            {
                var svc = ctx.HttpContext.RequestServices.GetRequiredService<MobileService>();
                if (!int.TryParse(ctx.Principal?.FindFirst("sub")?.Value, out var uid) || ctx.Principal?.FindFirst("typ")?.Value != "app" || !await svc.IsActiveAsync(uid))
                    ctx.Fail("inactive");
            },
            OnChallenge = async ctx =>
            {
                ctx.HandleResponse();
                ctx.Response.StatusCode = 401;
                await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("غير مصرح، سجّل الدخول"));
            }
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

builder.Services.Configure<Microsoft.Extensions.WebEncoders.WebEncoderOptions>(o => o.TextEncoderSettings = new System.Text.Encodings.Web.TextEncoderSettings(System.Text.Unicode.UnicodeRanges.All));
builder.Services.AddControllersWithViews(o => o.Filters.AddService<AdminScopeFilter>())
    .ConfigureApiBehaviorOptions(o => o.InvalidModelStateResponseFactory = ctx =>
    {
        var errors = ctx.ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(x => x.Key, x => x.Value!.Errors.Select(e => e.ErrorMessage).ToArray());
        return new BadRequestObjectResult(ApiResponse.Fail("البيانات المرسلة غير صالحة", errors));
    });
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = cfg.GetValue<long?>("Storage:MaxUploadBytes") ?? 20 * 1024 * 1024);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Hyundai Stock-Count Mobile API", Version = "v1", Description = "API تطبيق جرد هيونداي (المستخدمين الميدانيين). كل الاستجابات بالشكل { success, message, data, errors }." });
    c.DocInclusionPredicate((_, api) => api.RelativePath?.StartsWith("api/") == true);
    var scheme = new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header,
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { [scheme] = Array.Empty<string>() });
    var xml = Path.Combine(AppContext.BaseDirectory, "HyundaiStock.Web.xml");
    if (File.Exists(xml)) c.IncludeXmlComments(xml);
});

var app = builder.Build();
app.UseForwardedHeaders();
// Optional fallback for older relays without X-Forwarded-Proto. Match both the
// configured public host and the loopback transport; direct localhost stays HTTP.
var publicOrigin = cfg["ReverseProxy:PublicOrigin"];
if (!string.IsNullOrWhiteSpace(publicOrigin))
{
    if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" ||
        origin.AbsolutePath != "/" || !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment))
        throw new InvalidOperationException("ReverseProxy:PublicOrigin must be an HTTPS origin without a path or credentials.");
    app.Use(async (ctx, next) =>
    {
        if (ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
            string.Equals(ctx.Request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase))
            ctx.Request.Scheme = "https";
        await next();
    });
}

// ---------- Database init + seed
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInit");
    if (cfg.GetValue("Database:ApplyMigrationsOnStartup", true))
    {
        if (db.Database.IsSqlServer()) db.Database.Migrate();
        else
        {
            db.Database.EnsureCreated();
            await SqliteSchema.EnsureCurrentAsync(db);
        }
    }
    await DbSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordService>(),
        cfg.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions(), log);
    await DemoSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordService>(),
        cfg.GetSection("Seed").Get<SeedOptions>() ?? new SeedOptions(), app.Environment, log);
}

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    app.Logger.LogError(ex, "Unhandled exception");
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        ctx.Response.StatusCode = 500;
        await ctx.Response.WriteAsJsonAsync(ApiResponse.Fail("حدث خطأ غير متوقع"));
    }
    else if (ctx.Request.Path.StartsWithSegments("/Home/Error"))
    {
        ctx.Response.StatusCode = 500;
        ctx.Response.ContentType = "text/html; charset=utf-8";
        await ctx.Response.WriteAsync("<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><body><h1>حدث خطأ غير متوقع</h1><p><a href=\"/\">الرجوع</a></p></body></html>");
    }
    else ctx.Response.Redirect("/Home/Error");
}));
if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseStatusCodePages(async ctx =>
{
    var http = ctx.HttpContext;
    if (http.Request.Path.StartsWithSegments("/api"))
    {
        if (!http.Response.HasStarted && http.Response.ContentLength is null && string.IsNullOrEmpty(http.Response.ContentType))
            await http.Response.WriteAsJsonAsync(ApiResponse.Fail(http.Response.StatusCode == 404 ? "غير موجود" : http.Response.StatusCode == 429 ? "محاولات كثيرة، حاول بعد دقيقة" : "خطأ"));
        return;
    }
    if (http.Response.StatusCode == 404) http.Response.Redirect("/Home/NotFoundPage");
});

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseSwagger();
app.UseSwaggerUI(c => { c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mobile API v1"); c.RoutePrefix = "swagger"; });

app.MapControllers();
app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
