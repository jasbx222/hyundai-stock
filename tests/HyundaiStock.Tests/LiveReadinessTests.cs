using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HyundaiStock.Tests;

public sealed class LiveReadinessTests
{
    [Theory]
    [InlineData(null, 44.3)]
    [InlineData(33.3, null)]
    [InlineData(0d, 0d)]
    [InlineData(90.01, 44.3)]
    [InlineData(-90.01, 44.3)]
    [InlineData(33.3, 180.01)]
    [InlineData(33.3, -180.01)]
    [InlineData(double.NaN, 44d)]
    [InlineData(33d, double.PositiveInfinity)]
    public void Invalid_locations_have_a_clear_Arabic_error(double? lat, double? lng) =>
        Assert.Equal("تعذر تحديد موقعك، يرجى المحاولة مرة أخرى", Validators.GpsError(lat, lng));

    [Theory]
    [InlineData(0, 44)]
    [InlineData(33, 0)]
    [InlineData(-90, -180)]
    [InlineData(90, 180)]
    [InlineData(33.3152, 44.3661)]
    public void Valid_coordinate_boundaries_are_not_over_restricted(double lat, double lng) => Assert.Null(Validators.GpsError(lat, lng));

    [Fact]
    public async Task Every_location_API_rejects_invalid_GPS_without_persisting()
    {
        using var factory = new ApiFactory { Demo = true };
        var client = factory.CreateClient();
        await LoginMobile(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var live = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        var wh = await db.Warehouses.SingleAsync(x => x.Number == 9011);
        var part = await db.InventoryParts.SingleAsync(x => x.SessionId == live.Id && x.WarehouseId == wh.Id && x.PartNumber == "58101D3A01");
        var recount = await db.RecountRequests.SingleAsync(x => x.InventoryPart!.SessionId == live.Id);
        var beforeScans = await db.ScannedVins.CountAsync();
        var beforeNew = await db.NewPartRequests.CountAsync();
        foreach (var gps in new[] { (0d, 0d), (91d, 44d), (-91d, 44d), (33d, 181d), (33d, -181d) })
        {
            var bodies = new (string Path, object Body)[]
            {
                ("/api/v1/counts", new { partId = part.Id, quantity = 99, latitude = gps.Item1, longitude = gps.Item2 }),
                ("/api/v1/vins/scan", new { vin = DemoSeeder.Vin(1), warehouseId = wh.Id, latitude = gps.Item1, longitude = gps.Item2 }),
                ($"/api/v1/recounts/{recount.Id}/submit", new { quantity = 99, latitude = gps.Item1, longitude = gps.Item2 }),
                ("/api/v1/new-part-requests", new { partNumber = "BAD-GPS", description = "تجربة", quantity = 2, location = "A1", branchId = wh.BranchId, warehouseId = wh.Id, latitude = gps.Item1, longitude = gps.Item2 })
            };
            foreach (var (path, body) in bodies)
            {
                var response = await client.PostAsJsonAsync(path, body);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(json.GetProperty("success").GetBoolean());
                Assert.Contains("تعذر تحديد موقعك", json.GetProperty("message").GetString());
            }
        }
        Assert.Null((await db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == part.Id)).CountedQuantity);
        Assert.Equal(RecountStatus.Pending, (await db.RecountRequests.AsNoTracking().SingleAsync(x => x.Id == recount.Id)).Status);
        Assert.Equal(beforeScans, await db.ScannedVins.CountAsync());
        Assert.Equal(beforeNew, await db.NewPartRequests.CountAsync());
    }

    [Fact]
    public async Task Wrong_warehouse_and_closed_session_return_errors_without_writes()
    {
        using var factory = new ApiFactory { Demo = true };
        var client = factory.CreateClient();
        await LoginMobile(client);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var live = await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        var wh = await db.Warehouses.SingleAsync(x => x.Number == 9011);
        var other = await db.Warehouses.SingleAsync(x => x.Number == 9041);
        var part = await db.InventoryParts.SingleAsync(x => x.SessionId == live.Id && x.WarehouseId == wh.Id && x.PartNumber == "58101D3A01");
        var wrong = await client.PostAsJsonAsync("/api/v1/counts", new { partId = part.Id, warehouseId = other.Id, quantity = 99, latitude = 33.3, longitude = 44.4 });
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        var mismatch = await client.PostAsJsonAsync("/api/v1/new-part-requests", new { partNumber = "BAD-BRANCH", description = "تجربة", quantity = 2, location = "A1", warehouseId = wh.Id, branchId = other.BranchId, latitude = 33.3, longitude = 44.4 });
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        live.Status = SessionStatus.Completed;
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/counts", new { partId = part.Id, quantity = 99, latitude = 33.3, longitude = 44.4 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/vins/scan", new { vin = DemoSeeder.Vin(1), warehouseId = wh.Id, latitude = 33.3, longitude = 44.4 })).StatusCode);
        Assert.Null((await db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == part.Id)).CountedQuantity);
    }

    [Fact]
    public async Task Forwarded_HTTPS_preserves_redirect_login_and_secure_cookies()
    {
        using var factory = new LoopbackFactory { Demo = true };
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("http://hyundai.example") });
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var redirect = await client.GetAsync("/Requests");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("https", redirect.Headers.Location!.Scheme);
        Assert.Equal("hyundai.example", redirect.Headers.Location.Host);
        var login = await client.GetAsync(redirect.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var html = await login.Content.ReadAsStringAsync();
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        Assert.NotEmpty(token);
        var result = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"] = DemoSeeder.MainPhone, ["Password"] = "Test1234", ["ReturnUrl"] = "/Requests", ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.Equal("/Requests", result.Headers.Location!.OriginalString);
        Assert.Contains(result.Headers.GetValues("Set-Cookie"), x => x.StartsWith("hyst.auth=") && x.Contains("secure", StringComparison.OrdinalIgnoreCase));
        // Exercise all dashboard pages with the real authentication cookie.
        foreach (var path in new[] { "/", "/Users", "/Admins", "/Branches", "/Warehouses", "/Requests", "/NewParts", "/Parts", "/Finance", "/Cars", "/Sessions" })
            Assert.True((await client.GetAsync("https://hyundai.example" + path)).StatusCode == HttpStatusCode.OK, path);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("https://hyundai.example/VinRequests")).StatusCode);
    }

    [Fact]
    public async Task Local_HTTP_login_still_works_and_public_origin_fallback_does_not_downgrade()
    {
        using var factory = new LoopbackFactory { PublicOrigin = "https://hyundai.example" };
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("http://localhost/Account/Login")).StatusCode);
        var local = await client.GetAsync("http://localhost/Requests");
        Assert.Equal("http", local.Headers.Location!.Scheme);
        var publicRequest = await client.GetAsync("http://hyundai.example/Requests");
        Assert.Equal("https", publicRequest.Headers.Location!.Scheme);
    }

    [Fact]
    public async Task Untrusted_remote_cannot_spoof_forwarded_scheme_or_host()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("local.example");
        context.Request.Headers["X-Forwarded-Proto"] = "https";
        context.Request.Headers["X-Forwarded-Host"] = "evil.example";
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            Options.Create(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto, ForwardLimit = 1 }));
        await middleware.Invoke(context);
        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal("local.example", context.Request.Host.Value);
    }

    [Fact]
    public async Task Demo_is_idempotent_and_preserves_existing_passwords_counts_and_closed_sessions()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pw = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var existing = new Admin { Name = "موجود", PhoneNumber = "07700000000", Role = AdminRole.MainAdmin, PasswordHash = pw.Hash("Existing123") };
        db.Admins.Add(existing); await db.SaveChangesAsync();
        var old = new StocktakingSession { Year = 2026, StartedByAdminId = existing.Id, Status = SessionStatus.Completed, FileName = "existing.xlsx", StoredFileName = "existing.xlsx" };
        db.Sessions.Add(old); await db.SaveChangesAsync();
        var part = new InventoryPart { SessionId = old.Id, PartNumber = "OLD-1", CountedQuantity = 123 };
        db.InventoryParts.Add(part); await db.SaveChangesAsync();
        var opt = new SeedOptions { Demo = true, DemoPassword = "Different123" };
        await DemoSeeder.SeedAsync(db, pw, opt, env, NullLogger.Instance);
        var counts = new[] { await db.InventoryParts.CountAsync(), await db.Admins.CountAsync(), await db.Sessions.CountAsync(), await db.WorkflowAudits.CountAsync() };
        Assert.Equal(1204, counts[0]); // 400 stock lines + one approved addition, in each of three sessions, plus existing.
        Assert.Equal(await db.Sessions.CountAsync(), await db.Sessions.Select(x => x.Year).Distinct().CountAsync());
        Assert.Equal(5, await db.Branches.CountAsync());
        Assert.Equal(10, await db.Warehouses.CountAsync());
        Assert.True(pw.Verify(existing.PasswordHash, "Existing123"));
        Assert.Equal(123, (await db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == part.Id)).CountedQuantity);
        Assert.Equal(SessionStatus.Completed, (await db.Sessions.AsNoTracking().SingleAsync(x => x.Id == old.Id)).Status);
        Assert.Equal((await new SessionLookup(db).CurrentAsync())!.Id, (await new SessionLookup(db).LatestAsync())!.Id);
        await DemoSeeder.SeedAsync(db, pw, opt, env, NullLogger.Instance);
        Assert.Equal(counts, new[] { await db.InventoryParts.CountAsync(), await db.Admins.CountAsync(), await db.Sessions.CountAsync(), await db.WorkflowAudits.CountAsync() });
        var main = await db.Admins.SingleAsync(x => x.PhoneNumber == DemoSeeder.MainPhone);
        Assert.True(pw.Verify(main.PasswordHash, "Different123"));
        Assert.All(await db.ExpectedVins.Select(x => x.Vin).ToListAsync(), vin => Assert.True(Validators.IsVin(vin)));
    }

    [Fact]
    public async Task Demo_never_runs_in_production_even_with_flag()
    {
        using var factory = new ApiFactory { Demo = true, EnvironmentName = "Production" };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, await db.Admins.CountAsync());
        Assert.Equal(0, await db.Sessions.CountAsync());
    }

    [Fact]
    public async Task Demo_does_not_start_a_second_live_session()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pw = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var admin = new Admin { Name = "موجود", PhoneNumber = "07700000000", Role = AdminRole.MainAdmin, PasswordHash = pw.Hash("Existing123") };
        db.Admins.Add(admin); await db.SaveChangesAsync();
        var live = new StocktakingSession { Year = 2026, StartedByAdminId = admin.Id, FileName = "existing.xlsx", StoredFileName = "existing.xlsx" };
        db.Sessions.Add(live); await db.SaveChangesAsync();
        await DemoSeeder.SeedAsync(db, pw, new SeedOptions { Demo = true }, scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), NullLogger.Instance);
        Assert.Equal(live.Id, (await db.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress)).Id);
        Assert.Equal(3, await db.Sessions.CountAsync(x => x.Status == SessionStatus.Completed));
    }

    [Fact]
    public async Task Demo_collision_does_not_reset_or_expand_an_existing_account()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pw = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var existing = new AppUser { Name = "موجود", PhoneNumber = DemoSeeder.CounterPhone, PasswordHash = pw.Hash("Existing123") };
        db.AppUsers.Add(existing); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoSeeder.SeedAsync(db, pw, new SeedOptions { Demo = true }, scope.ServiceProvider.GetRequiredService<IHostEnvironment>(), NullLogger.Instance));
        Assert.True(pw.Verify(existing.PasswordHash, "Existing123"));
        Assert.Equal(0, await db.Branches.CountAsync());
    }

    internal static async Task LoginMobile(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = DemoSeeder.CounterPhone, password = "Test1234" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("data").GetProperty("accessToken").GetString());
    }
}

public sealed class LoopbackFactory : ApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(s => s.AddSingleton<IStartupFilter, LoopbackFilter>());
    }
    private sealed class LoopbackFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((ctx, nextStep) => { ctx.Connection.RemoteIpAddress = IPAddress.Loopback; return nextStep(ctx); });
            next(app);
        };
    }
}
