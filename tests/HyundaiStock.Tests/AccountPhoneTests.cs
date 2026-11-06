using System.Net;
using System.Net.Http.Json;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

public sealed class AccountPhoneTests : IDisposable
{
    private readonly AppDbContext db;
    private readonly PasswordService pw = new();
    private readonly AdminScope scope;
    private readonly Admin admin;
    private readonly AppUser user;
    private readonly Branch branch;
    private readonly Warehouse warehouse;

    public AccountPhoneTests()
    {
        db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        var main = new Admin { Name = "رئيسي", PhoneNumber = "07710000000", Role = AdminRole.MainAdmin, PasswordHash = pw.Hash("test-main") };
        branch = new Branch { Name = "فرع", Number = 1 };
        db.AddRange(main, branch); db.SaveChanges();
        scope = new(main.Id, main.Name, main.Role, null);
        warehouse = new Warehouse { Name = "مخزن", Number = 1, BranchId = branch.Id };
        admin = new Admin { Name = "مشرف", PhoneNumber = "+964 771-000-0001", Role = AdminRole.Admin, BranchId = branch.Id, PasswordHash = pw.Hash("admin-pass"), IsActive = true };
        user = new AppUser { Name = "عداد", PhoneNumber = "٠٧٧١٠٠٠٠٠٠٢", PasswordHash = pw.Hash("user-pass"), IsActive = true, CreatedByAdminId = main.Id };
        db.AddRange(warehouse, admin, user); db.SaveChanges();
    }

    private AdminInput AdminInput(string phone) => new() { Name = "مشرف جديد", PhoneNumber = phone, Password = "test-password", Role = AdminRole.Admin, BranchId = branch.Id };
    private UserInput UserInput(string phone) => new() { Name = "عداد جديد", PhoneNumber = phone, Password = "test-password", ConfirmPassword = "test-password", BranchIds = new() { branch.Id }, WarehouseIds = new() { warehouse.Id } };

    [Theory]
    [InlineData("٠٧٧١٠٠٠٠٠٠٢")]
    [InlineData("۰۷۷۱۰۰۰۰۰۰۲")]
    [InlineData("+964 771 000 0002")]
    [InlineData("00964-771-000-0002")]
    [InlineData("9647710000002")]
    [InlineData(" 0771 (000) 0002 ")]
    public void Phone_normalization_is_canonical(string phone) => Assert.Equal("07710000002", AccountPhones.Normalize(phone));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Create_rejects_duplicates_in_either_account_type_including_inactive(bool active)
    {
        admin.IsActive = active; user.IsActive = active; await db.SaveChangesAsync();
        var admins = new AdminService(db, pw); var users = new UserService(db, pw);
        Assert.Equal(AccountPhones.DuplicateError, (await admins.CreateAsync(AdminInput("07710000002"), scope))["PhoneNumber"]);
        Assert.Equal(AccountPhones.DuplicateError, (await users.SaveAsync(null, UserInput("07710000001"), scope))["PhoneNumber"]);
        Assert.Equal(AccountPhones.DuplicateError, (await admins.CreateAsync(AdminInput("07710000001"), scope))["PhoneNumber"]);
        Assert.Equal(AccountPhones.DuplicateError, (await users.SaveAsync(null, UserInput("07710000002"), scope))["PhoneNumber"]);
    }

    [Fact]
    public async Task Edit_rejects_cross_type_duplicates_but_allows_own_phone_and_persists_unique_changes()
    {
        var admins = new AdminService(db, pw); var users = new UserService(db, pw);
        Assert.Equal(AccountPhones.DuplicateError, (await admins.UpdateAsync(admin.Id, AdminInput("07710000002"), scope))["PhoneNumber"]);
        Assert.Equal(AccountPhones.DuplicateError, (await users.SaveAsync(user.Id, UserInput("07710000001"), scope))["PhoneNumber"]);
        Assert.Empty(await admins.UpdateAsync(admin.Id, AdminInput("07710000001"), scope));
        Assert.Empty(await users.SaveAsync(user.Id, UserInput("07710000002"), scope));
        Assert.Empty(await admins.UpdateAsync(admin.Id, AdminInput("+964 771 000 0003"), scope));
        Assert.Empty(await users.SaveAsync(user.Id, UserInput("+964 771 000 0004"), scope));
        Assert.Equal("07710000003", admin.PhoneNumber); Assert.Equal("07710000004", user.PhoneNumber);
    }

    [Fact]
    public async Task Legacy_admin_duplicate_does_not_block_employee_or_accept_admin_password()
    {
        admin.PhoneNumber = "+9647710000002"; await db.SaveChangesAsync();
        var mobile = new MobileService(db, new SessionLookup(db), pw);
        Assert.Equal(user.Id, (await mobile.LoginAsync("07710000002", "user-pass"))?.Id);
        Assert.Null(await mobile.LoginAsync("07710000002", "admin-pass"));
        Assert.True(await mobile.IsActiveAsync(user.Id));
        user.IsActive = false; await db.SaveChangesAsync();
        Assert.Null(await mobile.LoginAsync("07710000002", "user-pass"));
        Assert.False(await mobile.IsActiveAsync(user.Id));
    }

    [Fact]
    public async Task Ambiguous_legacy_employee_numbers_fail_closed()
    {
        db.AppUsers.Add(new AppUser { Name = "مكرر", PhoneNumber = "07710000002", PasswordHash = pw.Hash("user-pass"), IsActive = true });
        await db.SaveChangesAsync();
        Assert.Null(await new MobileService(db, new SessionLookup(db), pw).LoginAsync("07710000002", "user-pass"));
    }

    [Fact]
    public async Task Api_legacy_employee_login_and_authenticated_profile_survive_admin_duplicate()
    {
        using var factory = new ApiFactory();
        await using var services = factory.Services.CreateAsyncScope();
        var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Admins.Add(new Admin { Name = "مشرف", PhoneNumber = "07710000999", PasswordHash = pw.Hash("admin-pass"), IsActive = true });
        var employee = new AppUser { Name = "عداد", PhoneNumber = "07710000999", PasswordHash = pw.Hash("user-pass"), IsActive = true };
        context.AppUsers.Add(employee); await context.SaveChangesAsync();
        var client = factory.CreateClient();
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = employee.PhoneNumber, password = "admin-pass" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Contains("رقم الهاتف أو كلمة المرور", await wrong.Content.ReadAsStringAsync());
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = "+964 771 000 0999", password = "user-pass" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var data = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", data.GetProperty("data").GetProperty("accessToken").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_create_and_edit_show_Arabic_duplicate_phone_errors_without_writes()
    {
        using var factory = new ApiFactory { Demo = true };
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        static string Token(string html) => WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var loginHtml = await client.GetStringAsync("/Account/Login");
        var login = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PhoneNumber"] = DemoSeeder.MainPhone, ["Password"] = "Test1234", ["__RequestVerificationToken"] = Token(loginHtml)
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        await using var services = factory.Services.CreateAsyncScope();
        var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
        var counter = await context.AppUsers.SingleAsync(x => x.PhoneNumber == "07790000001");
        var targetAdmin = await context.Admins.FirstAsync(x => x.Role == AdminRole.Admin);
        var wh = await context.Warehouses.SingleAsync(x => x.Number == 9011);
        var before = (await context.Admins.CountAsync(), await context.AppUsers.CountAsync());
        foreach (var path in new[] { "/Admins/Create", $"/Admins/Edit/{targetAdmin.Id}", "/Users/Create", $"/Users/Edit/{counter.Id}" })
        {
            var form = await client.GetStringAsync(path);
            var phone = path.StartsWith("/Admins") ? counter.PhoneNumber : targetAdmin.PhoneNumber;
            var response = await client.PostAsync(path, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Name"] = "اختبار تكرار", ["PhoneNumber"] = phone, ["Password"] = "test-password", ["ConfirmPassword"] = "test-password",
                ["Role"] = ((int)AdminRole.Admin).ToString(), ["BranchId"] = wh.BranchId.ToString(), ["BranchIds"] = wh.BranchId.ToString(),
                ["WarehouseIds"] = wh.Id.ToString(), ["__RequestVerificationToken"] = Token(form)
            }));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(AccountPhones.DuplicateError, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }
        Assert.Equal(before, (await context.Admins.CountAsync(), await context.AppUsers.CountAsync()));
        Assert.Equal(counter.PhoneNumber, (await context.AppUsers.AsNoTracking().SingleAsync(x => x.Id == counter.Id)).PhoneNumber);
    }

    [Fact]
    public async Task Closed_session_parts_API_has_clear_nonfatal_conflict_and_no_data_corruption()
    {
        using var factory = new ApiFactory { Demo = true };
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = "07790000001", password = "Test1234" });
        var body = await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("data").GetProperty("accessToken").GetString());
        await using var services = factory.Services.CreateAsyncScope();
        var context = services.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await context.Sessions.SingleAsync(x => x.Status == SessionStatus.InProgress);
        session.Status = SessionStatus.Completed; await context.SaveChangesAsync();
        var current = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/v1/sessions/current");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, current.GetProperty("data").ValueKind);
        var parts = await client.GetAsync("/api/v1/parts?mine=true");
        Assert.Equal(HttpStatusCode.Conflict, parts.StatusCode);
        Assert.Contains("لا توجد جلسة جرد قيد التنفيذ", await parts.Content.ReadAsStringAsync());
    }

    public void Dispose() => db.Dispose();
}
