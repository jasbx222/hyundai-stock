using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    public string EnvironmentName { get; set; } = "Development";
    public bool Demo { get; set; }
    public string PublicOrigin { get; set; } = "";
    public string DbFile { get; } = Path.Combine(Path.GetTempPath(), $"hyst-api-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("environment", EnvironmentName);
        builder.UseSetting("Seed:Demo", Demo.ToString());
        builder.UseSetting("ReverseProxy:PublicOrigin", PublicOrigin);
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={DbFile}");
        builder.UseSetting("Database:ApplyMigrationsOnStartup", "true");
        builder.UseSetting("Jwt:Key", "local-dev-jwt-key-hyundai-stock-32b");
        builder.UseSetting("Jwt:Issuer", "HyundaiStock");
        builder.UseSetting("Jwt:Audience", "HyundaiStock.Mobile");
        builder.UseSetting("Seed:MainAdminPhone", "");
        builder.UseSetting("Seed:MainAdminPassword", "");
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:Demo"] = Demo.ToString(),
            ["ReverseProxy:PublicOrigin"] = PublicOrigin,
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Default"] = $"Data Source={DbFile}",
            ["Database:ApplyMigrationsOnStartup"] = "true",
            ["Jwt:Key"] = "local-dev-jwt-key-hyundai-stock-32b",
            ["Jwt:Issuer"] = "HyundaiStock",
            ["Jwt:Audience"] = "HyundaiStock.Mobile",
            ["Seed:MainAdminPhone"] = "",
            ["Seed:MainAdminPassword"] = ""
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try { if (File.Exists(DbFile)) File.Delete(DbFile); } catch { }
    }
}

public sealed class ApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ApiTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Mobile_login_preserves_legacy_employee_access_and_accepts_a_new_part_request()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pw = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var branch = new Branch { Name = "فرع", Number = 11 };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();
        var wh = new Warehouse { Name = "مخزن", Number = 11, BranchId = branch.Id, Type = WarehouseType.Type1 };
        var admin = new Admin { Name = "محاسب", PhoneNumber = "07720000001", PasswordHash = pw.Hash("secret"), Role = AdminRole.Accountant, IsActive = true };
        var main = new Admin { Name = "رئيسي", PhoneNumber = "07720000002", PasswordHash = pw.Hash("secret"), Role = AdminRole.MainAdmin, IsActive = true };
        db.Warehouses.Add(wh);
        db.Admins.AddRange(admin, main);
        var user = new AppUser { Name = "عداد", PhoneNumber = "07720000003", PasswordHash = pw.Hash("secret"), IsActive = true };
        var shadow = new AppUser { Name = "ظل", PhoneNumber = admin.PhoneNumber, PasswordHash = pw.Hash("employee-secret"), IsActive = true };
        db.AppUsers.AddRange(user, shadow);
        await db.SaveChangesAsync();
        db.AppUserWarehouses.Add(new AppUserWarehouse { AppUserId = user.Id, WarehouseId = wh.Id });
        db.Sessions.Add(new StocktakingSession { Year = 2026, StartedByAdminId = main.Id, FileName = "a.xlsx", StoredFileName = "a.xlsx" });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var denied = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = admin.PhoneNumber, password = "secret" });
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = user.PhoneNumber, password = "secret" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var token = body.GetProperty("data").GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var created = await client.PostAsJsonAsync("/api/v1/new-part-requests", new
        {
            partNumber = "API-1", description = "من التطبيق", branchId = branch.Id, warehouseId = wh.Id,
            location = "A", quantity = 2, latitude = 33.3, longitude = 44.4
        });
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, createdBody);
        Assert.Contains("تم إرسال طلب الإضافة", createdBody);
        Assert.Equal(0, await db.InventoryParts.CountAsync());

        var forbidden = await client.PostAsJsonAsync("/api/v1/new-part-requests/1/nope", new { });
        Assert.NotEqual(HttpStatusCode.OK, forbidden.StatusCode);
    }

    [Fact]
    public async Task Recount_form_disables_same_user_until_counted_and_lists_branch_counters()
    {
        using var factory = new ApiFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pw = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var branch = new Branch { Name = "بغداد", Number = 31 };
        var other = new Branch { Name = "البصرة", Number = 32 };
        db.Branches.AddRange(branch, other);
        await db.SaveChangesAsync();
        var wh = new Warehouse { Name = "بغداد النهضة", Number = 31, BranchId = branch.Id };
        var otherWh = new Warehouse { Name = "مخزن البصرة", Number = 32, BranchId = other.Id };
        db.Warehouses.AddRange(wh, otherWh);
        var admin = new Admin { Name = "رئيسي", PhoneNumber = "07731000001", PasswordHash = pw.Hash("secret"), Role = AdminRole.MainAdmin, IsActive = true };
        db.Admins.Add(admin);
        var counter = new AppUser { Name = "ياسر عمار", PhoneNumber = "07731000002", PasswordHash = pw.Hash("secret"), IsActive = true };
        var outsider = new AppUser { Name = "عداد البصرة", PhoneNumber = "07731000003", PasswordHash = pw.Hash("secret"), IsActive = true };
        db.AppUsers.AddRange(counter, outsider);
        await db.SaveChangesAsync();
        db.AppUserWarehouses.AddRange(
            new AppUserWarehouse { AppUserId = counter.Id, WarehouseId = wh.Id },
            new AppUserWarehouse { AppUserId = outsider.Id, WarehouseId = otherWh.Id });
        var session = new StocktakingSession { Year = 2026, StartedByAdminId = admin.Id, FileName = "a.xlsx", StoredFileName = "a.xlsx" };
        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        var open = new InventoryPart { SessionId = session.Id, PartNumber = "00H72AQ210", Description = "قطعة", SystemQuantity = 1, BranchId = branch.Id };
        var counted = new InventoryPart { SessionId = session.Id, PartNumber = "COUNTED-1", Description = "معدودة", SystemQuantity = 1, BranchId = branch.Id, WarehouseId = wh.Id, CountedQuantity = 4, CountedByUserId = counter.Id };
        db.InventoryParts.AddRange(open, counted);
        await db.SaveChangesAsync();

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        static string Token(string html) => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var loginPage = await client.GetStringAsync("/Account/Login");
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"] = admin.PhoneNumber, ["Password"] = "secret", ["__RequestVerificationToken"] = Token(loginPage)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var fresh = await client.GetStringAsync($"/Parts/RecountForm/{open.Id}");
        Assert.Contains("نفس المستخدم (لم تُعدّ بعد)", fresh);
        Assert.Contains("لم تُعدّ هذه القطعة بعد، لذا هذا الخيار غير متاح.", fresh);
        Assert.Contains("class=\"opt off\"", fresh);
        Assert.Contains("<option value=\"" + counter.Id + "\">ياسر عمار</option>", fresh);
        Assert.DoesNotContain("عداد البصرة", fresh);
        var same = System.Text.RegularExpressions.Regex.Match(fresh, "<input type=\"radio\" name=\"mode\" value=\"same\"[^>]*>").Value;
        var otherRadio = System.Text.RegularExpressions.Regex.Match(fresh, "<input type=\"radio\" name=\"mode\" value=\"other\"[^>]*>").Value;
        Assert.Contains("disabled", same);
        Assert.DoesNotContain("checked", same);
        Assert.Contains("checked", otherRadio);

        var again = await client.GetStringAsync($"/Parts/RecountForm/{counted.Id}");
        Assert.Contains("نفس المستخدم (ياسر عمار)", again);
        Assert.DoesNotContain("class=\"opt off\"", again);
        Assert.DoesNotContain("لم تُعدّ هذه القطعة بعد", again);
        var sameAgain = System.Text.RegularExpressions.Regex.Match(again, "<input type=\"radio\" name=\"mode\" value=\"same\"[^>]*>").Value;
        Assert.Contains("checked", sameAgain);
        Assert.DoesNotContain("disabled", sameAgain);
        Assert.Contains("<option value=\"" + counter.Id + "\">ياسر عمار</option>", again);
        Assert.DoesNotContain("عداد البصرة", again);
    }
}
