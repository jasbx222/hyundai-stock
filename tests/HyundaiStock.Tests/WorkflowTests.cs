using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace HyundaiStock.Tests;

public sealed class WorkflowTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"hyst-{Guid.NewGuid():N}.db");
    private readonly AppDbContext _db;
    private readonly PasswordService _pw = new();
    private readonly SessionLookup _sessions;
    private readonly MobileService _mobile;
    private readonly NewPartRequestService _newParts;
    private readonly QuantityRequestService _qty;
    private readonly PartService _parts;
    private readonly VinRequestService _vin;
    private readonly SessionService _sessionSvc;
    private readonly ExportService _export;

    private readonly Admin _main;
    private readonly Admin _spare;
    private readonly Admin _accountant;
    private readonly Admin _warehouse;
    private readonly Admin _branchAdmin;
    private readonly Admin _otherAccountant;
    private readonly AppUser _counter;
    private readonly Branch _branch;
    private readonly Branch _other;
    private readonly Warehouse _warehouseRow;
    private readonly StocktakingSession _session;
    private readonly InventoryPart _part;

    public WorkflowTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_file}").Options;
        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _sessions = new SessionLookup(_db);
        _mobile = new MobileService(_db, _sessions, _pw);
        _newParts = new NewPartRequestService(_db, _sessions);
        _qty = new QuantityRequestService(_db, _sessions);
        _parts = new PartService(_db, _sessions);
        _vin = new VinRequestService(_db);
        _sessionSvc = new SessionService(_db, new ExcelImportService(), Options.Create(new StorageOptions()), new TestHost());
        _export = new ExportService(_db, _sessions);

        _branch = new Branch { Name = "بغداد", Number = 1 };
        _other = new Branch { Name = "البصرة", Number = 2 };
        _db.Branches.AddRange(_branch, _other);
        _db.SaveChanges();
        _warehouseRow = new Warehouse { Name = "مخزن 1", Number = 1, BranchId = _branch.Id, Type = WarehouseType.Type1 };
        var otherWh = new Warehouse { Name = "مخزن 2", Number = 2, BranchId = _other.Id, Type = WarehouseType.Type1 };
        _db.Warehouses.AddRange(_warehouseRow, otherWh);
        _main = AdminOf("07710000001", AdminRole.MainAdmin, null);
        _spare = AdminOf("07710000002", AdminRole.SparePartsManager, _branch.Id);
        _accountant = AdminOf("07710000003", AdminRole.Accountant, null);
        _warehouse = AdminOf("07710000004", AdminRole.WarehouseManager, null);
        _branchAdmin = AdminOf("07710000005", AdminRole.Admin, _branch.Id);
        _otherAccountant = AdminOf("07710000006", AdminRole.Accountant, null);
        _db.Admins.AddRange(_main, _spare, _accountant, _warehouse, _branchAdmin, _otherAccountant);
        _db.SaveChanges();
        _db.AdminBranches.AddRange(new AdminBranch { AdminId = _accountant.Id, BranchId = _branch.Id }, new AdminBranch { AdminId = _otherAccountant.Id, BranchId = _other.Id });
        _db.AdminWarehouses.Add(new AdminWarehouse { AdminId = _warehouse.Id, WarehouseId = _warehouseRow.Id });
        _counter = new AppUser { Name = "عداد", PhoneNumber = "07733333333", PasswordHash = _pw.Hash("secret"), IsActive = true };
        var blocked = new AppUser { Name = "محاسب تطبيق", PhoneNumber = _accountant.PhoneNumber, PasswordHash = _pw.Hash("employee-secret"), IsActive = true };
        _db.AppUsers.AddRange(_counter, blocked);
        _db.SaveChanges();
        _db.AppUserWarehouses.Add(new AppUserWarehouse { AppUserId = _counter.Id, WarehouseId = _warehouseRow.Id });
        _session = new StocktakingSession { Year = 2026, Status = SessionStatus.InProgress, StartedByAdminId = _main.Id, FileName = "a.xlsx", StoredFileName = "a.xlsx" };
        _db.Sessions.Add(_session);
        _db.SaveChanges();
        _part = new InventoryPart { SessionId = _session.Id, PartNumber = "ABC-1", Description = "فلتر", Price = 10, SystemQuantity = 2, WarehouseId = _warehouseRow.Id, BranchId = _branch.Id, CountedQuantity = 2 };
        _db.InventoryParts.Add(_part);
        _db.SaveChanges();
    }

    [Fact]
    public async Task Counter_logs_in_and_admin_phone_does_not()
    {
        Assert.NotNull(await _mobile.LoginAsync(_counter.PhoneNumber, "secret"));
        Assert.Null(await _mobile.LoginAsync(_accountant.PhoneNumber, "secret"));
        Assert.NotNull(await _mobile.LoginAsync(_accountant.PhoneNumber, "employee-secret"));
        Assert.True(await _mobile.IsActiveAsync(_db.AppUsers.Single(x => x.PhoneNumber == _accountant.PhoneNumber).Id));
    }

    [Fact]
    public async Task Existing_count_works_and_unknown_part_is_not_created()
    {
        var ok = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 4, Location = "A1", Latitude = 33.3, Longitude = 44.3 });
        Assert.Null(ok.Error);
        Assert.Equal(4, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);

        var missing = await _mobile.CountAsync(_counter.Id, new CountInput { PartNumber = "NO-SUCH", WarehouseId = _warehouseRow.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(404, missing.Status);
        Assert.Equal("رمز القطعة غير موجود بالنظام", missing.Error);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
    }

    [Fact]
    public async Task New_part_rejects_bad_input_and_accepts_a_valid_request()
    {
        var gps = await _newParts.CreateAsync(_counter.Id, Input(latitude: null));
        Assert.Equal(400, gps.Status);
        var zero = await _newParts.CreateAsync(_counter.Id, Input(latitude: 0, longitude: 0));
        Assert.Contains("تعذر تحديد موقعك", zero.Error);
        var qty = await _newParts.CreateAsync(_counter.Id, Input(quantity: 0));
        Assert.Equal(400, qty.Status);
        var wrong = await _newParts.CreateAsync(_counter.Id, Input(warehouseId: 999));
        Assert.Equal(400, wrong.Status);
        var other = await _newParts.CreateAsync(_counter.Id, Input(branchId: _other.Id, warehouseId: _warehouseRow.Id));
        Assert.True(other.Status is 400 or 403);

        var created = await _newParts.CreateAsync(_counter.Id, Input());
        Assert.Equal(201, created.Status);
        Assert.Equal("بانتظار مدير قطع الغيار", created.Data!.Status);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());

        var dup = await _newParts.CreateAsync(_counter.Id, Input(partNumber: "zz 9"));
        Assert.Equal(409, dup.Status);
        Assert.Equal(NewPartRequestService.DuplicateMessage, dup.Error);
    }

    [Fact]
    public async Task Approval_chain_creates_one_inventory_row_and_blocks_the_wrong_role()
    {
        var created = await _newParts.CreateAsync(_counter.Id, Input());
        var id = created.Data!.Id;
        Assert.False((await _newParts.FinalApproveAsync(id, AccScope())).Ok);
        Assert.False((await _newParts.FinalApproveAsync(id, SpareScope())).Ok);
        Assert.True((await _newParts.ApproveAsync(id, SpareScope())).Ok);
        Assert.False((await _newParts.ApproveAsync(id, AccScope())).Ok);
        Assert.False((await _newParts.FinalApproveAsync(id, OtherAcc())).Ok);
        Assert.True((await _newParts.FinalApproveAsync(id, AccScope())).Ok);
        Assert.False((await _newParts.FinalApproveAsync(id, AccScope())).Ok);

        var line = await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.PartNumber == "ZZ-9");
        Assert.Equal(0, line.SystemQuantity);
        Assert.Equal(3, line.CountedQuantity);
        Assert.Equal(0, line.Price);
        Assert.Equal(_counter.Id, line.CountedByUserId);
        Assert.Equal(_warehouseRow.Id, line.WarehouseId);
        Assert.Equal("رف 1", line.CountedLocation);
        Assert.Contains(await _db.PartChangeLogs.AsNoTracking().Where(x => x.InventoryPartId == line.Id).Select(x => x.Source).ToListAsync(), x => x == "New Part Request");
        Assert.Equal(NewPartRequestStatus.Approved, (await _db.NewPartRequests.AsNoTracking().SingleAsync(x => x.Id == id)).Status);
        Assert.True(await _db.WorkflowAudits.AnyAsync(x => x.SubjectId == id && x.Action == AuditActions.FinalApproved && x.InventoryPartId == line.Id));
    }

    [Fact]
    public async Task Super_admin_can_final_approve_after_the_first_stage()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "SUP-1"))).Data!.Id;
        Assert.True((await _newParts.ApproveAsync(id, Super())).Ok);
        Assert.True((await _newParts.FinalApproveAsync(id, Super())).Ok);
        Assert.Equal(1, await _db.InventoryParts.CountAsync(x => x.PartNumber == "SUP-1"));
    }

    [Fact]
    public async Task Returned_request_can_be_cancelled_only_by_its_owner()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "RET-1"))).Data!.Id;
        Assert.False((await _newParts.ReturnAsync(id, "  ", SpareScope())).Ok);
        Assert.True((await _newParts.ReturnAsync(id, "الوصف ناقص", SpareScope())).Ok);
        var other = new AppUser { Name = "آخر", PhoneNumber = "07733333334", PasswordHash = _pw.Hash("secret") };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();
        Assert.Equal(404, (await _newParts.CancelAsync(other.Id, id)).Status);
        Assert.Equal(409, (await _newParts.CancelAsync(_counter.Id, (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "OPEN-1"))).Data!.Id)).Status);
        var cancelled = await _newParts.CancelAsync(_counter.Id, id);
        Assert.Null(cancelled.Error);
        Assert.Equal("ملغي", cancelled.Data!.Status);
        Assert.Equal(409, (await _newParts.CancelAsync(_counter.Id, id)).Status);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
        Assert.True(await _db.WorkflowAudits.AnyAsync(x => x.SubjectId == id && x.Action == AuditActions.Cancelled));
    }

    [Fact]
    public async Task Closed_session_rejects_new_requests_and_closes_open_ones()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "CLS-1"))).Data!.Id;
        Assert.True((await _sessionSvc.EndAsync(_session.Id, Super())).Ok);
        Assert.Equal(NewPartRequestStatus.SessionClosed, (await _db.NewPartRequests.AsNoTracking().SingleAsync(x => x.Id == id)).Status);
        var again = await _newParts.CreateAsync(_counter.Id, Input(partNumber: "CLS-2"));
        Assert.Equal(409, again.Status);
        Assert.False((await _newParts.FinalApproveAsync(id, Super())).Ok);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
    }

    [Fact]
    public async Task Quantity_correction_needs_both_stages_and_keeps_history()
    {
        var req = new QuantityCorrectionRequest
        {
            InventoryPartId = _part.Id, PartNumber = _part.PartNumber, PartDescription = _part.Description,
            OldQuantity = 2, NewQuantity = 9, Reason = "خطأ", BranchId = _branch.Id, WarehouseId = _warehouseRow.Id,
            RequestedByUserId = _counter.Id, Status = RequestStatus.Pending, ApprovalLevel = ApprovalLevel.None
        };
        _db.QuantityCorrections.Add(req);
        await _db.SaveChangesAsync();
        Assert.False((await _qty.FinalApproveAsync(req.Id, AccScope())).Ok);
        Assert.False((await _qty.ApproveAsync(req.Id, AccScope())).Ok);
        Assert.False((await _qty.RejectAsync(req.Id, "", WmScope())).Ok);
        Assert.True((await _qty.ApproveAsync(req.Id, WmScope())).Ok);
        Assert.True((await _qty.FinalApproveAsync(req.Id, AccScope())).Ok);
        Assert.Equal(9, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
        Assert.False((await _qty.FinalApproveAsync(req.Id, AccScope())).Ok);
    }

    [Fact]
    public async Task Branch_admin_still_approves_vin_and_recount_and_accountant_cannot_recount()
    {
        var scan = new ScannedVin { SessionId = _session.Id, Vin = "KMHD1111111111111", WarehouseId = _warehouseRow.Id, BranchId = _branch.Id, UserId = _counter.Id };
        _db.ScannedVins.Add(scan);
        await _db.SaveChangesAsync();
        var req = new VinCorrectionRequest { ScannedVinId = scan.Id, CurrentVin = scan.Vin, NewVin = "KMHD2222222222222", Reason = "خطأ قراءة", RequestedByUserId = _counter.Id };
        _db.VinCorrections.Add(req);
        await _db.SaveChangesAsync();
        Assert.False((await _vin.DecideAsync(req.Id, true, Super())).Ok);
        Assert.True((await _vin.DecideAsync(req.Id, true, new AdminScope(_branchAdmin.Id, _branchAdmin.Name, AdminRole.Admin, _branch.Id))).Ok);
        Assert.Equal("KMHD2222222222222", (await _db.ScannedVins.AsNoTracking().SingleAsync()).Vin);

        _part.CountedByUserId = _counter.Id;
        await _db.SaveChangesAsync();
        Assert.False((await _parts.RequestRecountAsync(_part.Id, _counter.Id, AccScope())).Ok);
        Assert.True((await _parts.RequestRecountAsync(_part.Id, _counter.Id, WmScope())).Ok);
    }

    [Fact]
    public async Task Finance_difference_uses_counted_minus_system_and_stays_in_scope()
    {
        var page = await _export.FinanceListAsync(new FinanceFilter { SessionId = _session.Id }, new PageQuery(1, 10), AccScope());
        var row = Assert.Single(page.Page!.Items);
        Assert.Equal(0, row.Difference);
        Assert.Equal(0m, row.FinancialDifference);

        _part.CountedQuantity = 5;
        await _db.SaveChangesAsync();
        page = await _export.FinanceListAsync(new FinanceFilter { SessionId = _session.Id, Difference = "surplus" }, new PageQuery(1, 10), AccScope());
        row = Assert.Single(page.Page!.Items);
        Assert.Equal(3, row.Difference);
        Assert.Equal(30m, row.FinancialDifference);

        var denied = await _export.FinanceListAsync(new FinanceFilter { SessionId = _session.Id, BranchId = _other.Id }, new PageQuery(1, 10), AccScope());
        Assert.True(denied.Forbidden);
        var file = await _export.FinanceFileAsync(new FinanceFilter { SessionId = _session.Id }, AccScope());
        Assert.NotNull(file.File);
        Assert.True(file.File!.Content.Length > 100);
    }

    [Fact]
    public async Task Returned_new_part_can_be_corrected_and_resubmitted_without_creating_a_second_request()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input())).Data!.Id;
        Assert.Equal(409, (await _newParts.ResubmitAsync(_counter.Id, id, Input())).Status);
        Assert.True((await _newParts.ReturnAsync(id, "صحح الوصف", SpareScope())).Ok);
        Assert.Equal(404, (await _newParts.ResubmitAsync(99999, id, Input())).Status);
        Assert.Equal(400, (await _newParts.ResubmitAsync(_counter.Id, id, Input(quantity: 0))).Status);
        var result = await _newParts.ResubmitAsync(_counter.Id, id, Input(partNumber: "FIXED-9", quantity: 7));
        Assert.Null(result.Error);
        Assert.Equal(id, result.Data!.Id);
        Assert.Equal("PendingSparePartsManager", result.Data.StatusCode);
        Assert.Equal(_warehouseRow.Id, result.Data.WarehouseId);
        Assert.Null(result.Data.ReturnReason);
        Assert.Equal(1, await _db.NewPartRequests.CountAsync());
        Assert.True((await _newParts.ApproveAsync(id, SpareScope())).Ok);
        Assert.True((await _newParts.FinalApproveAsync(id, AccScope())).Ok);
        Assert.Equal(7, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.PartNumber == "FIXED-9")).CountedQuantity);
        Assert.Equal(409, (await _newParts.ResubmitAsync(_counter.Id, id, Input())).Status);
    }

    [Fact]
    public async Task Counter_can_edit_own_quantity_request_only_before_first_approval()
    {
        var id = (await _mobile.QtyRequestAsync(_counter.Id, new QtyRequestInput { PartId = _part.Id, NewQuantity = 3, Reason = "خطأ" })).Data!.Id;
        Assert.Equal(404, (await _mobile.EditQtyRequestAsync(99999, id, new QtyRequestInput { NewQuantity = 8, Reason = "تصحيح" })).Status);
        Assert.Equal(400, (await _mobile.EditQtyRequestAsync(_counter.Id, id, new QtyRequestInput { NewQuantity = -1, Reason = "تصحيح" })).Status);
        var result = await _mobile.EditQtyRequestAsync(_counter.Id, id, new QtyRequestInput { NewQuantity = 8, Reason = "تصحيح" });
        Assert.Null(result.Error);
        Assert.True(result.Data!.CanEdit);
        Assert.Equal(8, result.Data.NewQuantity);
        Assert.True((await _qty.ApproveAsync(id, WmScope())).Ok);
        Assert.Equal(409, (await _mobile.EditQtyRequestAsync(_counter.Id, id, new QtyRequestInput { NewQuantity = 9, Reason = "متأخر" })).Status);
        Assert.False((await _mobile.MyQtyRequestsAsync(_counter.Id, new PageQuery(1,10))).Items.Single().CanEdit);
        Assert.True((await _qty.FinalApproveAsync(id, AccScope())).Ok);
        Assert.Equal(8, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
    }

    [Fact]
    public async Task Warehouse_manager_edits_scoped_quantity_requests_without_retargeting_the_part()
    {
        var id = (await _mobile.QtyRequestAsync(_counter.Id, new QtyRequestInput { PartId = _part.Id, NewQuantity = 3, Reason = "خطأ" })).Data!.Id;
        var err = await _qty.SaveAsync(id, new QtyInput { PartNumber = _part.PartNumber, PartDescription = _part.Description, NewQuantity = 7, Reason = "تعديل" }, WmScope());
        Assert.Empty(err);
        var saved = await _db.QuantityCorrections.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(_part.Id, saved.InventoryPartId); Assert.Equal(7, saved.NewQuantity);
        var denied = await _qty.SaveAsync(id, new QtyInput { PartNumber = _part.PartNumber, PartDescription = _part.Description, NewQuantity = 99 }, OtherAcc());
        Assert.NotEmpty(denied);
        Assert.True((await _qty.ApproveAsync(id, WmScope())).Ok);
        Assert.NotEmpty(await _qty.SaveAsync(id, new QtyInput { PartNumber = _part.PartNumber, NewQuantity = 9 }, WmScope()));
    }

    [Fact]
    public async Task Completed_recounts_remain_visible_when_mobile_requests_history()
    {
        _part.CountedByUserId = _counter.Id; await _db.SaveChangesAsync();
        Assert.True((await _parts.RequestRecountAsync(_part.Id, _counter.Id, WmScope())).Ok);
        var id = (await _mobile.RecountsAsync(_counter.Id, new PageQuery(1, 10))).Items.Single().Id;
        Assert.Null((await _mobile.SubmitRecountAsync(_counter.Id, id, new RecountInput { Quantity = 5, Location = "A1", Latitude = 33.3, Longitude = 44.3 })).Error);
        Assert.Empty((await _mobile.RecountsAsync(_counter.Id, new PageQuery(1, 10))).Items);
        var completed = (await _mobile.RecountsAsync(_counter.Id, new PageQuery(1, 10), true)).Items.Single();
        Assert.Equal("مكتمل", completed.Status); Assert.Equal(5, completed.ResultQuantity); Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task Warehouse_type_is_optional_and_existing_values_are_preserved()
    {
        var svc = new WarehouseService(_db);
        Assert.True((await svc.SaveAsync(null, "مخزن جديد", null, _branch.Id, 99)).Ok);
        Assert.Equal(WarehouseType.Type1, (await _db.Warehouses.AsNoTracking().SingleAsync(x => x.Number == 99)).Type);
        _warehouseRow.Type = WarehouseType.Type2; await _db.SaveChangesAsync();
        Assert.True((await svc.SaveAsync(_warehouseRow.Id, "مخزن معدل", null, _branch.Id, 1)).Ok);
        Assert.Equal(WarehouseType.Type2, (await _db.Warehouses.AsNoTracking().SingleAsync(x => x.Id == _warehouseRow.Id)).Type);
    }

    private NewPartInput Input(string partNumber = "ZZ-9", int? quantity = 3, int? branchId = null, int? warehouseId = null, double? latitude = 33.31, double? longitude = 44.36) => new()
    {
        PartNumber = partNumber, Description = "قطعة جديدة", BranchId = branchId ?? _branch.Id, WarehouseId = warehouseId ?? _warehouseRow.Id,
        Location = "رف 1", Quantity = quantity, Latitude = latitude, Longitude = longitude
    };

    private Admin AdminOf(string phone, AdminRole role, int? branchId) => new() { Name = role.ToString(), PhoneNumber = phone, PasswordHash = _pw.Hash("secret"), Role = role, BranchId = branchId, IsActive = true };
    private AdminScope SpareScope() => new(_spare.Id, _spare.Name, AdminRole.SparePartsManager, _branch.Id);
    private AdminScope AccScope() => new(_accountant.Id, _accountant.Name, AdminRole.Accountant, null, null, new[] { _branch.Id });
    private AdminScope OtherAcc() => new(_otherAccountant.Id, _otherAccountant.Name, AdminRole.Accountant, null, null, new[] { _other.Id });
    private AdminScope WmScope() => new(_warehouse.Id, _warehouse.Name, AdminRole.WarehouseManager, null, new[] { _warehouseRow.Id });
    private AdminScope Super() => new(_main.Id, _main.Name, AdminRole.SuperAdmin, null);

    private sealed class TestHost : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { File.Delete(_file); } catch { }
    }
}
