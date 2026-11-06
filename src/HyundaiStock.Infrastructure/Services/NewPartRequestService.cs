using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record NewPartRow(int Id, string Reference, string PartNumber, string Description, int Quantity, string Location, string BranchName, string WarehouseName, string EmployeeName, int SessionYear, double Latitude, double Longitude, NewPartRequestStatus Status, string? ReturnReason, DateTime CreatedAt, DateTime UpdatedAt);
public sealed record AuditRow(int Id, string Action, string? FromStatus, string ToStatus, string Actor, string ActorRole, string? Reason, DateTime CreatedAt);
public sealed record NewPartDetails(NewPartRow Request, List<AuditRow> History);

public sealed class NewPartInput
{
    public string? PartNumber { get; set; }
    public string? Description { get; set; }
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public string? Location { get; set; }
    public int? Quantity { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public sealed record MobileNewPart(
    int Id, string Reference, string PartNumber, string Description, int Quantity, string Location,
    string BranchName, string WarehouseName, string Status, string StatusCode, string? ReturnReason,
    DateTime CreatedAt, DateTime UpdatedAt, double Latitude, double Longitude, IReadOnlyList<MobileAudit> History);

public sealed record MobileAudit(string Action, string Status, string? Reason, DateTime At);

public sealed class NewPartRequestService(AppDbContext db, SessionLookup sessions)
{
    public const string DuplicateMessage = "يوجد طلب إضافة مفتوح لهذه القطعة";
    public const string ClosedMessage = "جلسة الجرد غير متاحة لإرسال الطلب";
    public const string DeniedMessage = "ليس لديك صلاحية لتنفيذ هذه العملية";
    public const string ProcessedMessage = "تم التعامل مع هذا الطلب مسبقاً";
    public const string PartExistsMessage = "القطعة موجودة في جلسة الجرد";

    public static string ReferenceOf(int id) => id.ToString("0000");

    private IQueryable<NewPartRequest> Scoped(AdminScope s)
    {
        var q = db.NewPartRequests.AsNoTracking();
        if (s.IsUnrestricted) return q;
        var branches = s.Branches.ToList();
        return q.Where(x => branches.Contains(x.BranchId));
    }

    public Task<PagedResult<NewPartRow>> ListAsync(PageQuery p, AdminScope s, NewPartRequestStatus? status)
    {
        var q = Scoped(s);
        if (status is NewPartRequestStatus st) q = q.Where(x => x.Status == st);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t) || x.Description.Contains(t) || x.RequestedByUser!.Name.Contains(t) || x.Warehouse!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new NewPartRow(
            x.Id, "", x.PartNumber, x.Description, x.Quantity, x.Location, x.Branch!.Name, x.Warehouse!.Name, x.RequestedByUser!.Name,
            x.Session!.Year, x.Latitude, x.Longitude, x.Status, x.ReturnReason, x.CreatedAt, x.UpdatedAt));
        var page = Paging.Build(rows, p);
        return Task.FromResult(new PagedResult<NewPartRow>
        {
            Items = page.Items.Select(x => x with { Reference = ReferenceOf(x.Id) }).ToList(),
            Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount
        });
    }

    public async Task<NewPartDetails?> GetAsync(int id, AdminScope s)
    {
        var row = await Scoped(s).Where(x => x.Id == id).Select(x => new NewPartRow(
            x.Id, "", x.PartNumber, x.Description, x.Quantity, x.Location, x.Branch!.Name, x.Warehouse!.Name, x.RequestedByUser!.Name,
            x.Session!.Year, x.Latitude, x.Longitude, x.Status, x.ReturnReason, x.CreatedAt, x.UpdatedAt)).FirstOrDefaultAsync();
        if (row == null) return null;
        var history = await db.WorkflowAudits.AsNoTracking().Where(x => x.Subject == AuditActions.NewPart && x.SubjectId == id)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => new AuditRow(x.Id, x.Action, x.FromStatus, x.ToStatus, x.ActorAdmin != null ? x.ActorAdmin.Name : x.ActorUser != null ? x.ActorUser.Name : "", x.ActorRole, x.Reason, x.CreatedAt)).ToListAsync();
        return new NewPartDetails(row with { Reference = ReferenceOf(row.Id) }, history);
    }

    public async Task<MobileResult<MobileNewPart>> CreateAsync(int userId, NewPartInput i)
    {
        var partNumber = i.PartNumber?.Trim() ?? "";
        var normalized = MobileService.NormalizePartCode(partNumber);
        var description = i.Description?.Trim() ?? "";
        var location = i.Location?.Trim() ?? "";
        if (normalized.Length == 0 || partNumber.Length > 64) return new(null, "رقم القطعة مطلوب", 400);
        if (description.Length == 0 || description.Length > 400) return new(null, "وصف القطعة مطلوب", 400);
        if (location.Length == 0 || location.Length > 64) return new(null, "الموقع مطلوب", 400);
        if (Validators.HasMarkup(partNumber) || Validators.HasMarkup(description) || Validators.HasMarkup(location)) return new(null, Validators.MarkupError, 400);
        if (i.Quantity is null or <= 0 or > 1000000) return new(null, "الكمية مطلوبة ويجب أن تكون أكبر من صفر", 400);
        if (Validators.GpsError(i.Latitude, i.Longitude) is string gps) return new(null, gps, 400);
        if (i.WarehouseId is not int wid || i.BranchId is not int bid) return new(null, "الفرع والمخزن مطلوبان", 400);

        var session = await sessions.CurrentAsync();
        if (session == null || session.Status != SessionStatus.InProgress) return new(null, ClosedMessage, 409);
        var wh = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == wid);
        if (wh == null) return new(null, "المخزن غير موجود", 400);
        if (wh.BranchId != bid) return new(null, "المخزن لا يتبع الفرع المحدد", 400);
        var allowed = await db.AppUserWarehouses.AnyAsync(x => x.AppUserId == userId && x.WarehouseId == wid);
        if (!allowed) return new(null, "المستودع غير مرتبط بحسابك", 403);

        if (await PartInSession(session.Id, normalized, wid, wh.BranchId)) return new(null, PartExistsMessage, 409);
        if (await ActiveExists(session.Id, wid, normalized)) return new(null, DuplicateMessage, 409);

        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = new NewPartRequest
        {
            PartNumber = partNumber, NormalizedPartNumber = normalized, Description = description, Quantity = i.Quantity.Value,
            Location = location, WarehouseId = wid, BranchId = wh.BranchId, SessionId = session.Id, RequestedByUserId = userId,
            Latitude = i.Latitude!.Value, Longitude = i.Longitude!.Value, Status = NewPartRequestStatus.PendingSparePartsManager, UpdatedAt = now
        };
        db.NewPartRequests.Add(r);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException ex) when (IsUnique(ex)) { await tx.RollbackAsync(); return new(null, DuplicateMessage, 409); }
        db.WorkflowAudits.Add(Audit(r, null, NewPartRequestStatus.PendingSparePartsManager, AuditActions.Submitted, null, userId, "Counter", null));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(await ToMobile(r.Id, userId), null, 201);
    }

    public async Task<PagedResult<MobileNewPart>> MineAsync(int userId, PageQuery p)
    {
        var q = db.NewPartRequests.AsNoTracking().Where(x => x.RequestedByUserId == userId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id);
        var total = await q.CountAsync();
        var ids = await q.Skip((p.SafePage - 1) * p.SafeSize).Take(p.SafeSize).Select(x => x.Id).ToListAsync();
        var items = new List<MobileNewPart>();
        foreach (var id in ids) items.Add((await ToMobile(id, userId))!);
        return new PagedResult<MobileNewPart> { Items = items, Page = p.SafePage, PageSize = p.SafeSize, TotalCount = total };
    }

    public async Task<MobileResult<MobileNewPart>> CancelAsync(int userId, int id)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.NewPartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.RequestedByUserId == userId);
        if (r == null) return new(null, "الطلب غير موجود", 404);
        if (r.Status != NewPartRequestStatus.ReturnedToEmployee) return new(null, r.Status is NewPartRequestStatus.Cancelled ? ProcessedMessage : "لا يمكن إلغاء الطلب في حالته الحالية", 409);
        var now = DateTime.UtcNow;
        var n = await db.NewPartRequests.Where(x => x.Id == id && x.RequestedByUserId == userId && x.Status == NewPartRequestStatus.ReturnedToEmployee)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, NewPartRequestStatus.Cancelled).SetProperty(x => x.CancelledAt, now).SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return new(null, ProcessedMessage, 409); }
        db.WorkflowAudits.Add(Audit(r, NewPartRequestStatus.ReturnedToEmployee, NewPartRequestStatus.Cancelled, AuditActions.Cancelled, null, userId, "Counter", null));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(await ToMobile(id, userId), null);
    }

    public async Task<OpResult> ApproveAsync(int id, AdminScope s)
    {
        if (!s.CanReviewNewPart) return OpResult.Fail(DeniedMessage);
        return await MoveAsync(id, s, NewPartRequestStatus.PendingSparePartsManager, NewPartRequestStatus.PendingAccountant, AuditActions.FirstApproved, null, first: true);
    }

    public async Task<OpResult> ReturnAsync(int id, string? reason, AdminScope s)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0) return OpResult.Fail("سبب الإرجاع مطلوب");
        if (why.Length > 1000) why = why[..1000];
        if (Validators.HasMarkup(why)) return OpResult.Fail(Validators.MarkupError);
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.NewPartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !s.IsUnrestricted && !s.AllowsBranch(r.BranchId)) return OpResult.Fail("الطلب غير موجود");
        var expected = r.Status == NewPartRequestStatus.PendingSparePartsManager ? s.CanReviewNewPart
            : r.Status == NewPartRequestStatus.PendingAccountant && s.CanFinalApproveNewPart;
        if (r.Status is not (NewPartRequestStatus.PendingSparePartsManager or NewPartRequestStatus.PendingAccountant) || !expected)
            return OpResult.Fail(r.Status is NewPartRequestStatus.PendingSparePartsManager or NewPartRequestStatus.PendingAccountant ? DeniedMessage : ProcessedMessage);
        var now = DateTime.UtcNow;
        var n = await db.NewPartRequests.Where(x => x.Id == id && x.Status == r.Status)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, NewPartRequestStatus.ReturnedToEmployee).SetProperty(x => x.ReturnReason, why).SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail(ProcessedMessage); }
        db.WorkflowAudits.Add(Audit(r, r.Status, NewPartRequestStatus.ReturnedToEmployee, AuditActions.Returned, s, null, s.Role.ToString(), why));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OpResult.Success();
    }

    public async Task<OpResult> FinalApproveAsync(int id, AdminScope s)
    {
        if (!s.CanFinalApproveNewPart) return OpResult.Fail(DeniedMessage);
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.NewPartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || (!s.IsUnrestricted && !s.AllowsBranch(r.BranchId))) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != NewPartRequestStatus.PendingAccountant || r.SparePartsApprovedAt == null) return OpResult.Fail(r.Status == NewPartRequestStatus.PendingAccountant ? "الطلب لم يجتز موافقة مدير قطع الغيار" : ProcessedMessage);
        var open = await db.Sessions.AnyAsync(x => x.Id == r.SessionId && x.Status == SessionStatus.InProgress);
        if (!open) return OpResult.Fail(ClosedMessage);
        if (await PartInSession(r.SessionId, r.NormalizedPartNumber, r.WarehouseId, r.BranchId)) return OpResult.Fail(PartExistsMessage);
        var now = DateTime.UtcNow;
        var n = await db.NewPartRequests.Where(x => x.Id == id && x.Status == NewPartRequestStatus.PendingAccountant && x.SparePartsApprovedAt != null)
            .ExecuteUpdateAsync(u => u
                .SetProperty(x => x.Status, NewPartRequestStatus.Approved)
                .SetProperty(x => x.AccountantApprovedByAdminId, s.Id)
                .SetProperty(x => x.AccountantApprovedAt, now)
                .SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail(ProcessedMessage); }
        var part = new InventoryPart
        {
            SessionId = r.SessionId, PartNumber = r.PartNumber, Description = r.Description, Price = 0, SystemQuantity = 0,
            WarehouseId = r.WarehouseId, BranchId = r.BranchId, CountedQuantity = r.Quantity, CountedLocation = r.Location,
            CountedByUserId = r.RequestedByUserId, CountedAt = now, Latitude = r.Latitude, Longitude = r.Longitude
        };
        part.ChangeLogs.Add(new PartChangeLog { Field = "إنشاء القطعة", NewValue = r.Quantity.ToString(), ChangedBy = s.Name, Source = "New Part Request" });
        db.InventoryParts.Add(part);
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateException) { await tx.RollbackAsync(); return OpResult.Fail("تعذر تثبيت الطلب"); }
        await db.NewPartRequests.Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.InventoryPartId, part.Id));
        db.WorkflowAudits.Add(Audit(r, NewPartRequestStatus.PendingAccountant, NewPartRequestStatus.Approved, AuditActions.FinalApproved, s, null, s.Role.ToString(), null, part.Id));
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException) { await tx.RollbackAsync(); return OpResult.Fail("تعذر تثبيت الطلب"); }
        return OpResult.Success(part.Id);
    }

    /// <summary>Called when a session ends. Open requests become SessionClosed and are not deleted.</summary>
    public async Task CloseForSessionAsync(int sessionId, AdminScope s)
    {
        var open = await db.NewPartRequests.Where(x => x.SessionId == sessionId &&
            (x.Status == NewPartRequestStatus.PendingSparePartsManager || x.Status == NewPartRequestStatus.PendingAccountant || x.Status == NewPartRequestStatus.ReturnedToEmployee)).ToListAsync();
        var now = DateTime.UtcNow;
        foreach (var r in open)
        {
            var prev = r.Status;
            r.Status = NewPartRequestStatus.SessionClosed;
            r.UpdatedAt = now;
            db.WorkflowAudits.Add(Audit(r, prev, NewPartRequestStatus.SessionClosed, AuditActions.SessionClosed, s, null, s.Role.ToString(), null));
        }
    }

    private async Task<OpResult> MoveAsync(int id, AdminScope s, NewPartRequestStatus from, NewPartRequestStatus to, string action, string? reason, bool first)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.NewPartRequests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !s.IsUnrestricted && !s.AllowsBranch(r.BranchId)) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != from) return OpResult.Fail(ProcessedMessage);
        var now = DateTime.UtcNow;
        var n = await db.NewPartRequests.Where(x => x.Id == id && x.Status == from).ExecuteUpdateAsync(u => u
            .SetProperty(x => x.Status, to)
            .SetProperty(x => x.UpdatedAt, now)
            .SetProperty(x => x.SparePartsApprovedByAdminId, s.Id)
            .SetProperty(x => x.SparePartsApprovedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail(ProcessedMessage); }
        db.WorkflowAudits.Add(Audit(r, from, to, action, s, null, s.Role.ToString(), reason));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return OpResult.Success();
    }

    private async Task<bool> PartInSession(int sessionId, string normalized, int warehouseId, int branchId) =>
        await db.InventoryParts.AnyAsync(x => x.SessionId == sessionId
            && x.PartNumber.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper() == normalized
            && (x.WarehouseId == warehouseId || (x.WarehouseId == null && x.BranchId == branchId)));

    private Task<bool> ActiveExists(int sessionId, int warehouseId, string normalized) =>
        db.NewPartRequests.AnyAsync(x => x.SessionId == sessionId && x.WarehouseId == warehouseId && x.NormalizedPartNumber == normalized
            && (x.Status == NewPartRequestStatus.PendingSparePartsManager || x.Status == NewPartRequestStatus.PendingAccountant || x.Status == NewPartRequestStatus.ReturnedToEmployee));

    private async Task<MobileNewPart?> ToMobile(int id, int userId)
    {
        var x = await db.NewPartRequests.AsNoTracking().Include(z => z.Branch).Include(z => z.Warehouse).FirstOrDefaultAsync(z => z.Id == id && z.RequestedByUserId == userId);
        if (x == null) return null;
        var history = await db.WorkflowAudits.AsNoTracking().Where(a => a.Subject == AuditActions.NewPart && a.SubjectId == id).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new MobileAudit(ActionLabel(a.Action), a.ToStatus, a.Reason, a.CreatedAt)).ToListAsync();
        var shown = history.Select(h => h with { Status = StatusLabel(h.Status) }).ToList();
        return new MobileNewPart(x.Id, ReferenceOf(x.Id), x.PartNumber, x.Description, x.Quantity, x.Location, x.Branch!.Name, x.Warehouse!.Name,
            Labels.NewPart(x.Status), x.Status.ToString(), x.ReturnReason, x.CreatedAt, x.UpdatedAt, x.Latitude, x.Longitude, shown);
    }

    private static string ActionLabel(string action) => action switch
    {
        AuditActions.Submitted => "تم الإرسال",
        AuditActions.FirstApproved => "موافقة مدير قطع الغيار",
        AuditActions.Returned => "تم الإرجاع",
        AuditActions.FinalApproved => "تثبيت المالية",
        AuditActions.Cancelled => "تم الإلغاء",
        AuditActions.SessionClosed => "انتهت جلسة الجرد",
        _ => action
    };

    private static string StatusLabel(string status) =>
        Enum.TryParse<NewPartRequestStatus>(status, out var s) ? Labels.NewPart(s) : status;

    private static WorkflowAudit Audit(NewPartRequest r, NewPartRequestStatus? from, NewPartRequestStatus to, string action, AdminScope? admin, int? userId, string role, string? reason, int? partId = null) => new()
    {
        Subject = AuditActions.NewPart, SubjectId = r.Id, NewPartRequestId = r.Id, Action = action,
        FromStatus = from?.ToString(), ToStatus = to.ToString(), ActorAdminId = admin?.Id, ActorUserId = userId, ActorRole = role,
        Reason = reason, SessionId = r.SessionId, WarehouseId = r.WarehouseId, BranchId = r.BranchId, InventoryPartId = partId ?? r.InventoryPartId
    };

    private static bool IsUnique(DbUpdateException ex)
    {
        var m = ex.InnerException?.Message ?? ex.Message;
        return m.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) || m.Contains("duplicate", StringComparison.OrdinalIgnoreCase);
    }
}
