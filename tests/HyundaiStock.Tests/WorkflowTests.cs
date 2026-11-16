using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var blocked = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 4, Location = "A1", Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(409, blocked.Status);
        var previous = Assert.IsType<CountedSnapshot>(blocked.Detail);
        Assert.Equal(2, previous.CountedQuantity);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
        var ok = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 1, Location = "A1", Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Null(ok.Error);
        Assert.Equal(1, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);

        var missing = await _mobile.CountAsync(_counter.Id, new CountInput { PartNumber = "NO-SUCH", WarehouseId = _warehouseRow.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(404, missing.Status);
        Assert.Equal("رمز القطعة غير موجود بالنظام", missing.Error);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
    }

    [Fact]
    public async Task Counts_Surplus_IsHeldUntilBothApprovals()
    {
        // _part: system 2, counted 2. A count of 5 is above the system quantity.
        var held = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 5, Location = "B-2", Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Null(held.Error);
        Assert.Equal(202, held.Status);
        Assert.Equal(MobileService.SurplusHeldMessage, held.Message);
        Assert.True(held.Data!.PendingApproval);
        Assert.Equal(2, held.Data.CountedQuantity);
        Assert.Equal(2, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
        var req = await _db.QuantityCorrections.AsNoTracking().SingleAsync(x => x.InventoryPartId == _part.Id);
        Assert.Equal(5, req.NewQuantity);
        Assert.Equal(2, req.OldQuantity);
        Assert.Equal(_counter.Id, req.RequestedByUserId);

        var again = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 6, Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Equal(409, again.Status);

        Assert.True((await _qty.ApproveAsync(req.Id, WmScope())).Ok);
        Assert.Equal(2, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
        Assert.True((await _qty.FinalApproveAsync(req.Id, AccScope())).Ok);
        Assert.Equal(5, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id)).CountedQuantity);
        Assert.False((await _mobile.PartAsync(_counter.Id, _part.Id)).Data!.PendingApproval);

        var equal = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 2, Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Equal(200, equal.Status);
        Assert.Equal(2, equal.Data!.CountedQuantity);
    }

    [Fact]
    public async Task Counts_Surplus_OnUncountedPartCreditsTheCounterOnApproval()
    {
        var open = new InventoryPart { SessionId = _session.Id, PartNumber = "SUR-1", Description = "قطعة", SystemQuantity = 1, WarehouseId = _warehouseRow.Id, BranchId = _branch.Id };
        _db.InventoryParts.Add(open);
        await _db.SaveChangesAsync();
        var held = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = open.Id, Quantity = 3, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(202, held.Status);
        Assert.Null((await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == open.Id)).CountedQuantity);
        var req = await _db.QuantityCorrections.AsNoTracking().SingleAsync(x => x.InventoryPartId == open.Id);
        Assert.True((await _qty.ApproveAsync(req.Id, WmScope())).Ok);
        Assert.True((await _qty.FinalApproveAsync(req.Id, AccScope())).Ok);
        var after = await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == open.Id);
        Assert.Equal(3, after.CountedQuantity);
        Assert.Equal(_counter.Id, after.CountedByUserId);
    }

    [Fact]
    public async Task Duplicate_part_number_keeps_the_uncounted_row_uncounted_until_it_is_saved()
    {
        var counted = new InventoryPart { SessionId = _session.Id, PartNumber = "DUP-1", Description = "قطعة مكررة", SystemQuantity = 1, SystemLocation = "A-1", WarehouseId = _warehouseRow.Id, BranchId = _branch.Id, CountedQuantity = 3, CountedLocation = "A-1" };
        var open = new InventoryPart { SessionId = _session.Id, PartNumber = "DUP-1", Description = "قطعة مكررة", SystemQuantity = 1, SystemLocation = "A-1", WarehouseId = _warehouseRow.Id, BranchId = _branch.Id };
        _db.InventoryParts.AddRange(counted, open);
        await _db.SaveChangesAsync();

        // Without the pick grant the counter sees only the default row (the uncounted one) and cannot count the other by id.
        var list = await _mobile.PartsAsync(_counter.Id, new PageQuery(1, 20), _warehouseRow.Id, null, code: "DUP-1");
        Assert.Null(list.Error);
        var only = Assert.Single(list.Data!.Items);
        Assert.Equal(open.Id, only.Id);
        Assert.Equal(2, only.RowNo);
        Assert.Equal(2, only.RowCount);
        var denied = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = counted.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Equal(403, denied.Status);
        Assert.Equal(MobileService.RowPickDenied, denied.Error);

        // With the grant every row is listed and numbered.
        _counter.CanPickDuplicateRow = true;
        await _db.SaveChangesAsync();
        var picked = await _mobile.PartsAsync(_counter.Id, new PageQuery(1, 20), _warehouseRow.Id, null, code: "DUP-1");
        var rows = picked.Data!.Items.OrderBy(x => x.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { 1, 2 }, rows.Select(x => x.RowNo).ToArray());
        Assert.Equal(3, rows.Single(x => x.Id == counted.Id).CountedQuantity);
        Assert.Null(rows.Single(x => x.Id == open.Id).CountedQuantity);
        _counter.CanPickDuplicateRow = false;
        await _db.SaveChangesAsync();

        var saved = await _mobile.CountAsync(_counter.Id, new CountInput { PartNumber = "DUP-1", WarehouseId = _warehouseRow.Id, Quantity = 1, Location = "A-1", Latitude = 33.3, Longitude = 44.3 });
        Assert.Null(saved.Error);
        Assert.Equal(open.Id, saved.Data!.Id);
        Assert.Equal(1, saved.Data.CountedQuantity);
        Assert.Equal(2, await _db.InventoryParts.CountAsync(x => x.PartNumber == "DUP-1"));
        Assert.Equal(3, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == counted.Id)).CountedQuantity);
        Assert.Equal(1, (await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == open.Id)).CountedQuantity);
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
        var exists = await _newParts.CreateAsync(_counter.Id, Input(partNumber: _part.PartNumber));
        Assert.Equal(409, exists.Status);
        Assert.Equal(NewPartRequestService.PartExistsMessage, exists.Error);

        var created = await _newParts.CreateAsync(_counter.Id, Input());
        Assert.Equal(201, created.Status);
        Assert.Equal("بانتظار مدير قطع الغيار", created.Data!.Status);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());

        var dup = await _newParts.CreateAsync(_counter.Id, Input(partNumber: "zz 9"));
        Assert.Equal(409, dup.Status);
        Assert.Equal(NewPartRequestService.DuplicateMessage, dup.Error);
    }

    [Fact]
    public async Task NewParts_Approve_SparePartsApprovalIsFinal()
    {
        var created = await _newParts.CreateAsync(_counter.Id, Input());
        var id = created.Data!.Id;
        Assert.False((await _newParts.ApproveAsync(id, AccScope())).Ok);
        Assert.Equal(1, await _db.InventoryParts.CountAsync());
        Assert.True((await _newParts.ApproveAsync(id, SpareScope())).Ok);
        Assert.False((await _newParts.ApproveAsync(id, SpareScope())).Ok);
        Assert.False((await _newParts.ReturnAsync(id, "متأخر", SpareScope())).Ok);

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
    public async Task NewParts_Approve_SuperAdminApprovesInOneStep()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "SUP-1"))).Data!.Id;
        Assert.True((await _newParts.ApproveAsync(id, Super())).Ok);
        Assert.Equal(1, await _db.InventoryParts.CountAsync(x => x.PartNumber == "SUP-1"));
    }

    [Fact]
    public async Task NewParts_Approve_LegacyAccountantRowIsFinishedBySpareParts()
    {
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "OLD-1"))).Data!.Id;
        await _db.NewPartRequests.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, NewPartRequestStatus.PendingAccountant));
        Assert.False((await _newParts.ApproveAsync(id, AccScope())).Ok);
        Assert.True((await _newParts.ApproveAsync(id, SpareScope())).Ok);
        Assert.Equal(1, await _db.InventoryParts.CountAsync(x => x.PartNumber == "OLD-1"));
    }

    [Fact]
    public async Task NewParts_Export_FollowsStatusAndYearFilter()
    {
        var approved = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "EXP-1"))).Data!.Id;
        await _newParts.CreateAsync(_counter.Id, Input(partNumber: "EXP-2"));
        Assert.True((await _newParts.ApproveAsync(approved, SpareScope())).Ok);

        var file = await _newParts.ExportAsync(SpareScope(), null, NewPartRequestStatus.Approved, _session.Year);
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(file.Content)))
        {
            var ws = wb.Worksheet("New Parts");
            Assert.Equal(2, ws.LastRowUsed()!.RowNumber());
            Assert.Equal("EXP-1", ws.Cell(2, 2).GetString());
            Assert.Equal(_branch.Name, ws.Cell(2, 6).GetString());
        }
        var all = await _newParts.ExportAsync(SpareScope(), "EXP", null, null);
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(all.Content)))
            Assert.Equal(3, wb.Worksheet("New Parts").LastRowUsed()!.RowNumber());
        var otherYear = await _newParts.ExportAsync(SpareScope(), null, null, _session.Year - 1);
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(otherYear.Content)))
            Assert.Equal(1, wb.Worksheet("New Parts").LastRowUsed()!.RowNumber());
    }

    [Fact]
    public async Task ApprovedParts_ListsOnlyApprovedAndExportsThem()
    {
        var approved = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "APR-1"))).Data!.Id;
        await _newParts.CreateAsync(_counter.Id, Input(partNumber: "APR-2"));
        Assert.True((await _newParts.ApproveAsync(approved, SpareScope())).Ok);

        var page = await _newParts.ApprovedListAsync(new PageQuery(1, 10), SpareScope(), null);
        var row = Assert.Single(page.Items);
        Assert.Equal("APR-1", row.PartNumber);
        Assert.NotNull(row.ApprovedAt);
        Assert.NotNull(row.InventoryPartId);
        Assert.Empty((await _newParts.ApprovedListAsync(new PageQuery(1, 10), SpareScope(), _session.Year - 1)).Items);

        var file = await _newParts.ApprovedExportAsync(SpareScope(), null, _session.Year);
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(file.Content));
        var ws = wb.Worksheet("New Parts");
        Assert.Equal(2, ws.LastRowUsed()!.RowNumber());
        Assert.Equal("APR-1", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public async Task ListExports_ReturnEveryRowOfThePageQuery()
    {
        for (int i = 0; i < 12; i++) _db.AppUsers.Add(new AppUser { Name = $"مستخدم {i}", PhoneNumber = $"0775000{i:0000}", PasswordHash = "x", IsActive = true });
        await _db.SaveChangesAsync();
        var xl = new ListExports(new BranchService(_db), new WarehouseService(_db), new AdminService(_db, _pw), new UserService(_db, _pw), _qty, _parts, _vin, _sessionSvc);
        var users = await xl.UsersAsync(null, Super());
        using (var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(users.Content)))
            Assert.True(wb.Worksheet("Users").LastRowUsed()!.RowNumber() > 11);
        foreach (var f in new[] { await xl.AdminsAsync(null, Super()), await xl.BranchesAsync(null, Super()), await xl.WarehousesAsync(null, null, Super()),
            await xl.QuantityRequestsAsync(null, null, Super()), await xl.RecountsAsync(null, null, Super()), await xl.VinRequestsAsync(null, null, Super()), await xl.SessionsAsync(null) })
        {
            using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(f.Content));
            Assert.True(wb.Worksheets.First().LastRowUsed()!.RowNumber() >= 1);
        }
    }

    [Fact]
    public async Task Lists_YearFilter_PicksThatYearsSession()
    {
        var (thisYear, s1) = await _parts.ListAsync(new PageQuery(1, 10), Super(), null, _session.Year);
        Assert.Equal(_session.Id, s1!.Id);
        Assert.Single(thisYear.Items);
        var (none, s2) = await _parts.ListAsync(new PageQuery(1, 10), Super(), null, 1999);
        Assert.Null(s2);
        Assert.Empty(none.Items);
        var id = (await _newParts.CreateAsync(_counter.Id, Input(partNumber: "YR-1"))).Data!.Id;
        Assert.Single((await _newParts.ListAsync(new PageQuery(1, 10), Super(), null, _session.Year)).Items, x => x.Id == id);
        Assert.Empty((await _newParts.ListAsync(new PageQuery(1, 10), Super(), null, 1999)).Items);
    }

    [Fact]
    public async Task Sessions_Start_AccountsManagerMayStartAccountantMayNot()
    {
        var manager = new AdminScope(_accountant.Id, _accountant.Name, AdminRole.AccountsManager, null);
        Assert.True(manager.CanStartSession);
        Assert.False(AccScope().CanStartSession);
        Assert.False(SpareScope().CanStartSession);
        var denied = await _sessionSvc.StartAsync(2031, new MemoryStream(new byte[] { 1 }), "x.xlsx", 1, AccScope());
        Assert.False(denied.Ok);
        Assert.Contains("مدير الحسابات", denied.Error);
        var allowed = await _sessionSvc.StartAsync(2031, new MemoryStream(new byte[] { 1 }), "x.xlsx", 1, manager);
        Assert.DoesNotContain("مدير الحسابات", allowed.Error ?? "");
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
        Assert.False((await _newParts.ApproveAsync(id, Super())).Ok);
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
    public async Task Branch_admin_still_approves_vin_and_accountant_can_request_recount()
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
        Assert.False((await _parts.RequestRecountAsync(_part.Id, _counter.Id, SpareScope())).Ok);
        Assert.True((await _parts.RequestRecountAsync(_part.Id, _counter.Id, AccScope())).Ok);
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
        var pending = (await _mobile.RecountsAsync(_counter.Id, new PageQuery(1, 10))).Items.Single();
        Assert.Equal(_part.BranchId, pending.BranchId);
        Assert.Equal(_branch.Name, pending.BranchName);
        Assert.Equal(_warehouseRow.Id, pending.WarehouseId);
        var again = await _mobile.SubmitRecountAsync(_counter.Id, id, new RecountInput { Quantity = 5, Location = "A1", Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(409, again.Status);
        Assert.Null((await _mobile.SubmitRecountAsync(_counter.Id, id, new RecountInput { Quantity = 5, Location = "A1", Latitude = 33.3, Longitude = 44.3, Confirm = true })).Error);
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

    [Fact]
    public async Task Counted_and_difference_lists_are_filtered_before_they_are_returned()
    {
        var spare = await _parts.ListAsync(new PageQuery(1, 10), SpareScope(), "counted");
        var row = Assert.Single(spare.page.Items);
        Assert.Equal(0, row.SystemQuantity);
        Assert.Equal("مطابق", row.Indicator);
        var hidden = await _parts.GetAsync(_part.Id, SpareScope());
        Assert.NotNull(hidden);
        Assert.Equal(0, hidden!.Part.SystemQuantity);
        Assert.Equal(0, hidden.Part.Price);
        Assert.Equal("مطابق", hidden.Indicator);

        _part.CountedQuantity = 0;
        await _db.SaveChangesAsync();
        Assert.Single((await _parts.ListAsync(new PageQuery(1, 10), Super(), "counted")).page.Items);
        Assert.Single((await _parts.ListAsync(new PageQuery(1, 10), Super(), "diff")).page.Items);

        _part.CountedQuantity = null;
        await _db.SaveChangesAsync();
        Assert.Empty((await _parts.ListAsync(new PageQuery(1, 10), Super(), "counted")).page.Items);
        Assert.Empty((await _parts.ListAsync(new PageQuery(1, 10), Super(), "diff")).page.Items);
    }

    [Fact]
    public async Task All_branches_is_explicit_and_null_branch_stays_closed()
    {
        var closed = new AdminScope(9, "فرع", AdminRole.Admin, null);
        Assert.False(closed.SeesAllBranches);
        Assert.Empty((await _parts.ListAsync(new PageQuery(1, 10), closed)).page.Items);

        var open = new AdminScope(9, "كل", AdminRole.SparePartsManager, null, AllBranches: true);
        Assert.True(open.SeesAllBranches);
        Assert.Single((await _parts.ListAsync(new PageQuery(1, 10), open)).page.Items);

        var svc = new AdminService(_db, _pw);
        var created = await svc.CreateAsync(new AdminInput { Name = "كل الفروع", PhoneNumber = "07710000077", Password = "secret1", Role = AdminRole.Admin, AllBranches = true }, Super());
        Assert.Empty(created);
        var saved = await _db.Admins.SingleAsync(x => x.PhoneNumber == "07710000077");
        Assert.True(saved.AllBranches);
        Assert.Null(saved.BranchId);

        var tampered = await svc.CreateAsync(new AdminInput { Name = "محاولة", PhoneNumber = "07710000078", Password = "secret1", Role = AdminRole.MainAdmin, AllBranches = true, BranchId = _branch.Id }, new AdminScope(_branchAdmin.Id, _branchAdmin.Name, AdminRole.Admin, _branch.Id));
        Assert.Empty(tampered);
        var limited = await _db.Admins.SingleAsync(x => x.PhoneNumber == "07710000078");
        Assert.Equal(AdminRole.Admin, limited.Role);
        Assert.False(limited.AllBranches);
        Assert.Equal(_branch.Id, limited.BranchId);
    }

    [Fact]
    public async Task Recount_candidates_follow_the_part_warehouse_or_its_branch()
    {
        var otherWh = await _db.Warehouses.SingleAsync(x => x.BranchId == _other.Id);
        var sibling = new Warehouse { Name = "مخزن مجاور", Number = 9, BranchId = _branch.Id, Type = WarehouseType.Type1 };
        _db.Warehouses.Add(sibling);
        await _db.SaveChangesAsync();
        var local = new AppUser { Name = "عداد الفرع", PhoneNumber = "07755555551", PasswordHash = _pw.Hash("secret"), IsActive = true };
        var siblingUser = new AppUser { Name = "مخزن مجاور", PhoneNumber = "07755555555", PasswordHash = _pw.Hash("secret"), IsActive = true };
        var outsider = new AppUser { Name = "عداد آخر", PhoneNumber = "07755555552", PasswordHash = _pw.Hash("secret"), IsActive = true };
        var inactive = new AppUser { Name = "متوقف", PhoneNumber = "07755555553", PasswordHash = _pw.Hash("secret"), IsActive = false };
        var branchOnly = new AppUser { Name = "بدون مخزن", PhoneNumber = "07755555554", PasswordHash = _pw.Hash("secret"), IsActive = true };
        _db.AppUsers.AddRange(local, siblingUser, outsider, inactive, branchOnly);
        await _db.SaveChangesAsync();
        _db.AppUserWarehouses.AddRange(
            new AppUserWarehouse { AppUserId = local.Id, WarehouseId = _warehouseRow.Id },
            new AppUserWarehouse { AppUserId = siblingUser.Id, WarehouseId = sibling.Id },
            new AppUserWarehouse { AppUserId = outsider.Id, WarehouseId = otherWh.Id },
            new AppUserWarehouse { AppUserId = inactive.Id, WarehouseId = _warehouseRow.Id });
        _db.AppUserBranches.Add(new AppUserBranch { AppUserId = branchOnly.Id, BranchId = _branch.Id });
        var part = new InventoryPart { SessionId = _session.Id, PartNumber = "00H72AQ210", Description = "قطعة فرع", SystemQuantity = 1, BranchId = _branch.Id };
        _db.InventoryParts.Add(part);
        await _db.SaveChangesAsync();

        var branchIds = (await _parts.CandidateUsersAsync(part.Id)).Select(x => x.Id).ToList();
        Assert.Contains(_counter.Id, branchIds);
        Assert.Contains(local.Id, branchIds);
        Assert.Contains(siblingUser.Id, branchIds);
        Assert.DoesNotContain(outsider.Id, branchIds);
        Assert.DoesNotContain(inactive.Id, branchIds);
        Assert.DoesNotContain(branchOnly.Id, branchIds);
        Assert.True((await _parts.RequestRecountAsync(part.Id, siblingUser.Id, AccScope())).Ok);
        Assert.False((await _parts.RequestRecountAsync(part.Id, outsider.Id, AccScope())).Ok);
        Assert.False((await _parts.RequestRecountAsync(part.Id, inactive.Id, AccScope())).Ok);
        Assert.False((await _parts.RequestRecountAsync(part.Id, branchOnly.Id, AccScope())).Ok);
        Assert.False((await _parts.RequestRecountAsync(part.Id, null, AccScope())).Ok);

        var pinnedIds = (await _parts.CandidateUsersAsync(_part.Id)).Select(x => x.Id).ToList();
        Assert.Contains(_counter.Id, pinnedIds);
        Assert.Contains(local.Id, pinnedIds);
        Assert.DoesNotContain(siblingUser.Id, pinnedIds);
        Assert.DoesNotContain(outsider.Id, pinnedIds);
        Assert.False((await _parts.RequestRecountAsync(_part.Id, siblingUser.Id, AccScope())).Ok);
        _part.CountedByUserId = _counter.Id;
        await _db.SaveChangesAsync();
        Assert.True((await _parts.RequestRecountAsync(_part.Id, null, AccScope())).Ok);
        Assert.Equal(_counter.Id, (await _db.RecountRequests.SingleAsync(x => x.InventoryPartId == _part.Id)).AssignedUserId);
    }

    [Fact]
    public async Task Main_admin_can_assign_another_main_admin()
    {
        var svc = new AdminService(_db, _pw);
        var main = new AdminScope(_main.Id, _main.Name, AdminRole.MainAdmin, null);
        var err = await svc.CreateAsync(new AdminInput { Name = "مسؤول ثاني", PhoneNumber = "07710000079", Password = "secret1", Role = AdminRole.MainAdmin }, main);
        Assert.Empty(err);
        Assert.Equal(AdminRole.MainAdmin, (await _db.Admins.SingleAsync(x => x.PhoneNumber == "07710000079")).Role);
    }

    [Fact]
    public async Task Read_only_roles_cannot_change_quantities_and_finance_can_request_recount()
    {
        var vehicle = new AdminScope(8, "سيارات", AdminRole.VehicleWarehouseManager, _branch.Id);
        Assert.False(vehicle.CanRequestRecount);
        Assert.False(vehicle.CanFirstApproveQty);
        Assert.True(vehicle.HidesStockFigures);
        var denied = await _qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 3, Reason = "لا" }, vehicle);
        Assert.Contains(denied, x => x.Value.Contains("صلاحية"));

        _part.CountedByUserId = _counter.Id;
        await _db.SaveChangesAsync();
        Assert.False((await _parts.RequestRecountAsync(_part.Id, null, vehicle)).Ok);
        var accounts = new AdminScope(_accountant.Id, _accountant.Name, AdminRole.AccountsManager, null, null, new[] { _branch.Id });
        Assert.True(accounts.CanFinalApproveQty);
        Assert.False(accounts.CanFirstApproveQty);
        Assert.True((await _parts.RequestRecountAsync(_part.Id, null, accounts)).Ok);
        Assert.False((await _parts.RequestRecountAsync(_part.Id, null, accounts)).Ok);
        var row = await _db.RecountRequests.SingleAsync();
        Assert.Equal(_accountant.Id, row.RequestedByAdminId);
        Assert.Equal(_counter.Id, row.AssignedUserId);
        Assert.Equal(RecountStatus.Pending, row.Status);
    }

    [Fact]
    public async Task One_warehouse_is_resolved_and_its_branch_is_taken_from_the_warehouse()
    {
        var counted = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Null(counted.Error);
        var scanned = await _mobile.ScanVinAsync(_counter.Id, new ScanInput { Vin = "KMHD1111111111111", Latitude = 33.3, Longitude = 44.3 });
        Assert.Null(scanned.Error);
        Assert.Equal(_warehouseRow.BranchId, (await _db.ScannedVins.SingleAsync()).BranchId);

        var second = new Warehouse { Name = "مخزن إضافي", Number = 3, BranchId = _branch.Id, Type = WarehouseType.Type1 };
        _db.Warehouses.Add(second);
        await _db.SaveChangesAsync();
        _db.AppUserWarehouses.Add(new AppUserWarehouse { AppUserId = _counter.Id, WarehouseId = second.Id });
        await _db.SaveChangesAsync();
        var byPart = await _mobile.CountAsync(_counter.Id, new CountInput { PartId = _part.Id, Quantity = 2, Latitude = 33.3, Longitude = 44.3, Confirm = true });
        Assert.Null(byPart.Error);
        var saved = await _db.InventoryParts.AsNoTracking().SingleAsync(x => x.Id == _part.Id);
        Assert.Equal(2, saved.CountedQuantity);
        Assert.Equal(_warehouseRow.Id, saved.WarehouseId);
        var byCode = await _mobile.CountAsync(_counter.Id, new CountInput { PartNumber = _part.PartNumber, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(400, byCode.Status);
        Assert.Equal("اختر المستودع", byCode.Error);
        var foreign = await _mobile.ScanVinAsync(_counter.Id, new ScanInput { Vin = "KMHD2222222222222", WarehouseId = 999, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(403, foreign.Status);

        var stranger = new AppUser { Name = "بدون مخزن", PhoneNumber = "07744444444", PasswordHash = _pw.Hash("secret"), IsActive = true };
        _db.AppUsers.Add(stranger);
        await _db.SaveChangesAsync();
        var none = await _mobile.CountAsync(stranger.Id, new CountInput { PartId = _part.Id, Quantity = 1, Latitude = 33.3, Longitude = 44.3 });
        Assert.Equal(400, none.Status);
    }

    [Fact]
    public void New_part_details_hide_geographic_coordinates()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "HyundaiStock.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "src", "HyundaiStock.Web", "Views", "NewParts", "Details.cshtml"));
        Assert.DoesNotContain("الموقع الجغرافي", text);
        Assert.Contains("الموقع", text);
    }

    [Fact]
    public async Task Device_token_follows_the_authenticated_user_and_push_failure_keeps_the_request()
    {
        var token = "device-token-1234567890";
        Assert.Null((await _mobile.RegisterDeviceAsync(_counter.Id, token, "android")).Error);
        Assert.Null((await _mobile.RegisterDeviceAsync(_counter.Id, "second-device-token-123456", "android")).Error);
        Assert.Equal(2, await _db.DeviceRegistrations.CountAsync(x => x.AppUserId == _counter.Id && x.IsActive));
        var other = await _db.AppUsers.SingleAsync(x => x.PhoneNumber == _accountant.PhoneNumber);
        Assert.Null((await _mobile.RegisterDeviceAsync(other.Id, token, "android")).Error);
        var owner = await _db.DeviceRegistrations.SingleAsync(x => x.Token == token);
        Assert.Equal(other.Id, owner.AppUserId);
        await _mobile.DeactivateDeviceAsync(_counter.Id, token);
        Assert.True((await _db.DeviceRegistrations.SingleAsync(x => x.Token == token)).IsActive);
        await _mobile.DeactivateDeviceAsync(other.Id, token);
        Assert.False((await _db.DeviceRegistrations.SingleAsync(x => x.Token == token)).IsActive);

        var parts = new PartService(_db, _sessions, new BoomNotifier());
        _part.CountedByUserId = _counter.Id;
        await _db.SaveChangesAsync();
        Assert.True((await parts.RequestRecountAsync(_part.Id, _counter.Id, WmScope())).Ok);
        var previous = Environment.GetEnvironmentVariable("FCM_SERVER_KEY");
        Environment.SetEnvironmentVariable("FCM_SERVER_KEY", null);
        try
        {
            var notifier = new StockNotifier(_db, LoggerFactory.Create(_ => { }).CreateLogger<StockNotifier>(), new ConfigurationBuilder().Build(), new NoHttp());
            await notifier.RecountAssignedAsync(_counter.Id, 1, _part.PartNumber);
            await notifier.SessionStartedAsync(_session.Id);
        }
        finally { Environment.SetEnvironmentVariable("FCM_SERVER_KEY", previous); }
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

    [Fact]
    public async Task Quantity_request_reaches_one_user_or_the_warehouse_users()
    {
        var sent = new List<int>();
        var qty = new QuantityRequestService(_db, _sessions, null, new CaptureNotifier(sent));
        var missing = await qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 4 }, Super());
        Assert.Contains("اختر مستخدمًا أو فرعًا أو مخزنًا", missing.Values);

        var otherWh = await _db.Warehouses.SingleAsync(x => x.BranchId == _other.Id);
        var outsider = new AppUser { Name = "خارج", PhoneNumber = "07744444444", PasswordHash = _pw.Hash("secret"), IsActive = true };
        _db.AppUsers.Add(outsider);
        await _db.SaveChangesAsync();
        _db.AppUserWarehouses.Add(new AppUserWarehouse { AppUserId = outsider.Id, WarehouseId = otherWh.Id });
        await _db.SaveChangesAsync();

        var wrong = await qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 4, Audience = "user", AudienceUserId = outsider.Id }, Super());
        Assert.Contains("المستخدم غير مرتبط بهذا المخزن أو الفرع", wrong.Values);

        sent.Clear();
        var one = await qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 4, Audience = "user", AudienceUserId = _counter.Id }, Super());
        Assert.Empty(one);
        var saved = await _db.QuantityCorrections.OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(1, saved.AudienceKind);
        Assert.Equal(_counter.Id, saved.AudienceUserId);
        Assert.Equal(new[] { _counter.Id }, sent);
        Assert.True((await qty.ApproveAsync(saved.Id, WmScope())).Ok);

        sent.Clear();
        var many = await qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 5, Audience = "warehouse", AudienceWarehouseId = _warehouseRow.Id }, Super());
        Assert.Empty(many);
        var broad = await _db.QuantityCorrections.OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(3, broad.AudienceKind);
        Assert.Null(broad.AudienceUserId);
        Assert.Equal(new[] { _counter.Id }, sent);
        Assert.DoesNotContain(outsider.Id, sent);

        var mismatch = await qty.SaveAsync(null, new QtyInput { InventoryPartId = _part.Id, NewQuantity = 6, Audience = "warehouse", AudienceWarehouseId = otherWh.Id }, Super());
        Assert.Contains("المخزن لا يطابق مخزن القطعة", mismatch.Values);
    }

    private sealed class CaptureNotifier(List<int> sent) : IStockNotifier
    {
        public Task SessionStartedAsync(int sessionId) => Task.CompletedTask;
        public Task RecountAssignedAsync(int userId, int recountId, string partNumber) => Task.CompletedTask;
        public Task QuantityRequestedAsync(IReadOnlyCollection<int> userIds, int requestId, string partNumber)
        {
            sent.AddRange(userIds);
            return Task.CompletedTask;
        }
    }

    private sealed class BoomNotifier : IStockNotifier
    {
        public Task SessionStartedAsync(int sessionId) => throw new InvalidOperationException("down");
        public Task RecountAssignedAsync(int userId, int recountId, string partNumber) => throw new InvalidOperationException("down");
        public Task QuantityRequestedAsync(IReadOnlyCollection<int> userIds, int requestId, string partNumber) => throw new InvalidOperationException("down");
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("provider was called");
    }

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
