using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Tests;

public sealed class RecountListTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"hyst-recount-{Guid.NewGuid():N}.db");
    private readonly AppDbContext _db;
    private readonly PartService _parts;
    private readonly Admin _main;
    private readonly AppUser _counter;
    private readonly Branch _branch;
    private readonly Branch _other;
    private readonly Warehouse _warehouse;
    private readonly InventoryPart _part;

    public RecountListTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_file}").Options);
        _db.Database.EnsureCreated();
        _parts = new PartService(_db, new SessionLookup(_db));
        _branch = new Branch { Name = "بغداد", Number = 1 };
        _other = new Branch { Name = "البصرة", Number = 2 };
        _db.Branches.AddRange(_branch, _other);
        _db.SaveChanges();
        _warehouse = new Warehouse { Name = "النهضة", Number = 1, BranchId = _branch.Id };
        _db.Warehouses.Add(_warehouse);
        _main = new Admin { Name = "المسؤول الرئيسي", PhoneNumber = "07700000001", PasswordHash = "x", Role = AdminRole.MainAdmin, IsActive = true };
        _db.Admins.Add(_main);
        _counter = new AppUser { Name = "ياسر", PhoneNumber = "07711111111", PasswordHash = "x", IsActive = true };
        _db.AppUsers.Add(_counter);
        _db.SaveChanges();
        _db.AppUserWarehouses.Add(new AppUserWarehouse { AppUserId = _counter.Id, WarehouseId = _warehouse.Id });
        var session = new StocktakingSession { Year = 2026, Status = SessionStatus.InProgress, StartedByAdminId = _main.Id, FileName = "a.xlsx", StoredFileName = "a.xlsx" };
        _db.Sessions.Add(session);
        _db.SaveChanges();
        _part = new InventoryPart { SessionId = session.Id, PartNumber = "263203N000", Description = "فلتر", WarehouseId = _warehouse.Id, BranchId = _branch.Id, CountedQuantity = 50, CountedByUserId = _counter.Id };
        _db.InventoryParts.Add(_part);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Parts_Recount_AppearsOnRecountListForTheCreator()
    {
        var main = new AdminScope(_main.Id, _main.Name, AdminRole.MainAdmin, null);
        var created = await _parts.RequestRecountAsync(_part.Id, null, main);
        Assert.True(created.Ok, created.Error);

        var page = await _parts.RecountsAsync(new PageQuery(1, 10), main);
        var row = Assert.Single(page.Items);
        Assert.Equal("263203N000", row.PartNumber);
        Assert.Equal("المسؤول الرئيسي", row.RequestedBy);
        Assert.Equal("ياسر", row.AssignedTo);
        Assert.Equal(RecountStatus.Pending, row.Status);
        Assert.Null(row.ResultQuantity);

        var manager = new AdminScope(8, "أمين", AdminRole.WarehouseManager, null, new[] { _warehouse.Id });
        Assert.Equal(row.Id, Assert.Single((await _parts.RecountsAsync(new PageQuery(1, 10), manager)).Items).Id);
        var branchAdmin = new AdminScope(9, "فرع", AdminRole.Admin, _branch.Id);
        Assert.Equal(row.Id, Assert.Single((await _parts.RecountsAsync(new PageQuery(1, 10), branchAdmin)).Items).Id);

        var closed = new AdminScope(10, "بدون فرع", AdminRole.Admin, null);
        Assert.Empty((await _parts.RecountsAsync(new PageQuery(1, 10), closed)).Items);
        var otherBranch = new AdminScope(11, "بصرة", AdminRole.Accountant, null, null, new[] { _other.Id });
        Assert.Empty((await _parts.RecountsAsync(new PageQuery(1, 10), otherBranch)).Items);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_file); } catch { }
    }
}
