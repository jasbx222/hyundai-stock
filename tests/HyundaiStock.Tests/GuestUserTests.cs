using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

/// <summary>A guest mobile user opens the app and sees its warehouses, but counts, scans and requests nothing. Any mobile user may delete their own account.</summary>
public sealed class GuestUserTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"hyst-guest-{Guid.NewGuid():N}.db");
    private readonly AppDbContext _db;
    private readonly PasswordService _pw = new();
    private readonly MobileService _mobile;
    private readonly NewPartRequestService _newParts;
    private readonly AppUser _guest;
    private readonly AppUser _counter;
    private readonly Branch _branch;
    private readonly Warehouse _wh;
    private readonly InventoryPart _part;
    private const string Vin = "KMHD84LF5JU123456";

    public GuestUserTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_file}").Options);
        _db.Database.EnsureCreated();
        var sessions = new SessionLookup(_db);
        _mobile = new MobileService(_db, sessions, _pw);
        _newParts = new NewPartRequestService(_db, sessions);

        _branch = new Branch { Name = "بغداد", Number = 1 };
        _db.Branches.Add(_branch); _db.SaveChanges();
        _wh = new Warehouse { Name = "مخزن 1", Number = 1, BranchId = _branch.Id, Type = WarehouseType.Type1 };
        _db.Warehouses.Add(_wh);
        var main = new Admin { Name = "رئيسي", PhoneNumber = "07710000001", PasswordHash = _pw.Hash("secret"), Role = AdminRole.MainAdmin, IsActive = true };
        _db.Admins.Add(main);
        _guest = new AppUser { Name = "ضيف", PhoneNumber = "07744444444", PasswordHash = _pw.Hash("secret"), IsActive = true, IsGuest = true };
        _counter = new AppUser { Name = "عداد", PhoneNumber = "07733333333", PasswordHash = _pw.Hash("secret"), IsActive = true };
        _db.AppUsers.AddRange(_guest, _counter); _db.SaveChanges();
        _db.AppUserWarehouses.AddRange(new AppUserWarehouse { AppUserId = _guest.Id, WarehouseId = _wh.Id }, new AppUserWarehouse { AppUserId = _counter.Id, WarehouseId = _wh.Id });
        var session = new StocktakingSession { Year = 2026, Name = "جرد سنوي 2026", Status = SessionStatus.InProgress, StartedByAdminId = main.Id, FileName = "a.xlsx", StoredFileName = "a.xlsx" };
        _db.Sessions.Add(session); _db.SaveChanges();
        _part = new InventoryPart { SessionId = session.Id, PartNumber = "ABC-1", Description = "فلتر", SystemQuantity = 2, WarehouseId = _wh.Id, BranchId = _branch.Id };
        _db.InventoryParts.Add(_part);
        _db.ScannedVins.Add(new ScannedVin { SessionId = session.Id, Vin = Vin, UserId = _counter.Id, WarehouseId = _wh.Id, BranchId = _branch.Id, Latitude = 33.3, Longitude = 44.3 });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Guest_logs_in_and_sees_parts_and_scanned_vins_of_its_warehouses()
    {
        Assert.NotNull(await _mobile.LoginAsync(_guest.PhoneNumber, "secret"));
        var me = (await _mobile.ProfileAsync(_guest.Id))!;
        Assert.True(me.IsGuest);
        Assert.True(me.CanCountParts); Assert.True(me.CanScanVins);
        Assert.False((await _mobile.ProfileAsync(_counter.Id))!.IsGuest);

        var parts = await _mobile.PartsAsync(_guest.Id, new PageQuery(1, 10, null), null, null);
        Assert.Null(parts.Error);
        Assert.Equal(_part.Id, Assert.Single(parts.Data!.Items).Id);
        Assert.Null((await _mobile.PartAsync(_guest.Id, _part.Id)).Error);
        Assert.Null((await _mobile.LookupVinAsync(_guest.Id, Vin)).Error);
        Assert.Equal(Vin, Assert.Single((await _mobile.MyVinsAsync(_guest.Id, new PageQuery(1, 10, null))).Items).Vin);
    }

    [Fact]
    public async Task Guest_cannot_count_scan_or_send_requests()
    {
        var count = await _mobile.CountAsync(_guest.Id, new CountInput { PartId = _part.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(403, count.Status); Assert.Equal(Guests.Denied, count.Error);
        var scan = await _mobile.ScanVinAsync(_guest.Id, new ScanInput { Vin = "KMHD84LF5JU654321", WarehouseId = _wh.Id, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(403, scan.Status);
        Assert.Equal(403, (await _mobile.AskRecountAsync(_guest.Id, _part.Id)).Status);
        Assert.Equal(403, (await _mobile.VinCorrectionAsync(_guest.Id, new VinCorrectionInput { ScannedVinId = 1, NewVin = "KMHD84LF5JU654321", Reason = "x" })).Status);
        Assert.Equal(403, (await _mobile.SubmitRecountAsync(_guest.Id, 1, new RecountInput { Quantity = 1, Latitude = 33.3, Longitude = 44.3 })).Status);
        var np = await _newParts.CreateAsync(_guest.Id, new NewPartInput { PartNumber = "NEW-1", Description = "جديدة", BranchId = _branch.Id, WarehouseId = _wh.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(403, np.Status);
        Assert.Equal(403, (await _newParts.CancelAsync(_guest.Id, 1)).Status);

        Assert.Null((await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
        Assert.Single(await _db.ScannedVins.ToListAsync());
        Assert.Empty(await _db.NewPartRequests.ToListAsync());
        Assert.Empty(await _db.RecountAsks.ToListAsync());
    }

    [Fact]
    public async Task Admin_marks_a_user_as_guest_from_the_dashboard()
    {
        var users = new UserService(_db, _pw);
        var super = new AdminScope(1, "رئيسي", AdminRole.MainAdmin, null);
        var input = new UserInput { Name = "زائر", PhoneNumber = "07755555555", Password = "secret1", ConfirmPassword = "secret1", BranchIds = new() { _branch.Id }, WarehouseIds = new() { _wh.Id }, IsGuest = true };
        Assert.Empty(await users.SaveAsync(null, input, super));
        var u = await _db.AppUsers.AsNoTracking().SingleAsync(x => x.PhoneNumber == "07755555555");
        Assert.True(u.IsGuest);
        Assert.True((await users.GetAsync(u.Id, super))!.IsGuest);
        Assert.Equal("ضيف (مشاهدة فقط)", Labels.UserCountLabel(u.CountType, u.IsGuest));
        input.Password = null; input.ConfirmPassword = null; input.IsGuest = false;
        Assert.Empty(await users.SaveAsync(u.Id, input, super));
        Assert.False((await _db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == u.Id)).IsGuest);
    }

    [Fact]
    public async Task Api_me_reports_guest_and_counts_return_403()
    {
        using var factory = new ApiFactory { Demo = true };
        var mobile = factory.CreateClient();
        await LiveReadinessTests.LoginMobile(mobile);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.AppUsers.SingleAsync(x => x.PhoneNumber == DemoSeeder.CounterPhone);
        user.IsGuest = true;
        await db.SaveChangesAsync();

        var me = (await mobile.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("data");
        Assert.True(me.GetProperty("isGuest").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/v1/parts")).StatusCode);
        var partId = await db.InventoryParts.Select(x => x.Id).FirstAsync();
        var count = await mobile.PostAsJsonAsync("/api/v1/counts", new { partId, quantity = 1, latitude = 33.3, longitude = 44.4 });
        Assert.Equal(HttpStatusCode.Forbidden, count.StatusCode);
        var body = await count.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal(Guests.Denied, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task User_deletes_own_account_and_keeps_count_history()
    {
        var counted = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 2, Latitude = 33.3, Longitude = 44.3 });
        Assert.Null(counted.Error);
        _db.DeviceRegistrations.Add(new DeviceRegistration { AppUserId = _counter.Id, Token = new string('t', 40), Platform = "android" });
        await _db.SaveChangesAsync();

        Assert.Equal(400, (await _mobile.DeleteAccountAsync(_counter.Id, "wrong")).Status);
        Assert.True(await _mobile.IsActiveAsync(_counter.Id));

        Assert.Null((await _mobile.DeleteAccountAsync(_counter.Id, "secret")).Error);
        Assert.False(await _mobile.IsActiveAsync(_counter.Id));
        Assert.Null(await _mobile.LoginAsync("07733333333", "secret"));
        var u = await _db.AppUsers.AsNoTracking().SingleAsync(x => x.Id == _counter.Id);
        Assert.NotNull(u.DeletedAt); Assert.Equal(MobileService.DeletedPhone(u.Id), u.PhoneNumber); Assert.Equal("", u.PasswordHash);
        Assert.False(await _db.DeviceRegistrations.AnyAsync(x => x.AppUserId == _counter.Id && x.IsActive));
        Assert.Equal(_counter.Id, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedByUserId);
        Assert.Equal(404, (await _mobile.DeleteAccountAsync(_counter.Id, "secret")).Status);

        // Gone from the dashboard, and the phone number is free for a new account.
        var users = new UserService(_db, _pw);
        var super = new AdminScope(1, "رئيسي", AdminRole.MainAdmin, null);
        Assert.DoesNotContain((await users.ListAsync(new PageQuery(1, 50, null), super)).Items, x => x.Id == _counter.Id);
        Assert.Null(await users.GetAsync(_counter.Id, super));
        Assert.False((await users.SetActiveAsync(_counter.Id, true, super)).Ok);
        var again = new UserInput { Name = "عداد جديد", PhoneNumber = "07733333333", Password = "secret1", ConfirmPassword = "secret1", BranchIds = new() { _branch.Id }, WarehouseIds = new() { _wh.Id } };
        Assert.Empty(await users.SaveAsync(null, again, super));
    }

    [Fact]
    public async Task Api_delete_account_then_the_token_stops_working()
    {
        using var factory = new ApiFactory { Demo = true };
        var mobile = factory.CreateClient();
        await LiveReadinessTests.LoginMobile(mobile);
        var bad = await mobile.PostAsJsonAsync("/api/v1/me/delete", new { password = "not-the-password" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await mobile.GetAsync("/api/v1/me")).StatusCode);

        var ok = await mobile.PostAsJsonAsync("/api/v1/me/delete", new { password = "Test1234" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("success").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await mobile.GetAsync("/api/v1/me")).StatusCode);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = DemoSeeder.CounterPhone, password = "Test1234" });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_file); } catch { }
    }
}
