using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record PartRow(int Id, string PartNumber, string Description, int? CountedQuantity, int SystemQuantity, string? BranchName, string? WarehouseName, string? UserName, int? CountedByUserId, string? Indicator = null);
public sealed record RecountListRow(int Id, string PartNumber, string RequestedBy, string AssignedTo, DateTime CreatedAt, RecountStatus Status, int? ResultQuantity);
public sealed record ChangeLogRow(int Id, string Field, string? OldValue, string? NewValue, string ChangedBy, string Source, DateTime CreatedAt);
public sealed record RecountRow(int Id, string UserName, string RequestedBy, RecountStatus Status, int? ResultQuantity, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record VinRow(int Id, string Vin, string SessionName, string UserName, string WarehouseName, string BranchName, double? Latitude, double? Longitude, DateTime CreatedAt, string? VehicleType = null, int? ManufactureYear = null);
public sealed record MissingVinRow(int Id, string Vin, string? VehicleType, int? ManufactureYear, string? WarehouseName, string? BranchName, string SessionName, DateTime CreatedAt);
public sealed record VinReqRow(int Id, string CurrentVin, string NewVin, string Reason, string RequestedBy, string? ApprovedBy, RequestStatus Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record SessionRow(int Id, int Year, SessionStatus Status, string StartedBy, DateTime StartedAt, DateTime? EndedAt, string FileName, int Parts, int Vins, string Name = "", SessionKind Kind = SessionKind.Annual, int? Month = null);

public sealed class PartDetails
{
    public InventoryPart Part { get; init; } = null!;
    public List<RecountRow> Recounts { get; init; } = new();
    public List<ChangeLogRow> Logs { get; init; } = new();
    public string? Indicator { get; init; }
}

public sealed class SessionLookup(AppDbContext db)
{
    public Task<StocktakingSession?> CurrentAsync() => db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.InProgress).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
    public Task<StocktakingSession?> LatestAsync() => db.Sessions.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
    /// <summary>Every session (annual and monthly), newest first, by its display name. Feeds the session filter on every page.</summary>
    public Task<List<NamedItem>> OptionsAsync() => db.Sessions.AsNoTracking().OrderByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).Select(x => new NamedItem(x.Id, x.Name, null)).ToListAsync();
    /// <summary>The chosen session, or the latest one when none is chosen. An unknown id returns null.</summary>
    public async Task<StocktakingSession?> ForSessionAsync(int? id) => id is int sid
        ? await db.Sessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid)
        : await LatestAsync();
    /// <summary>Older links filtered by year: the annual session of that year, else its newest monthly one. A year with no session returns null.</summary>
    public async Task<StocktakingSession?> ForYearAsync(int? year) => year is int y
        ? await db.Sessions.AsNoTracking().Where(x => x.Year == y).OrderBy(x => x.Kind).ThenByDescending(x => x.StartedAt).FirstOrDefaultAsync()
        : await LatestAsync();
    /// <summary>Session id for a page filter: the chosen session, else the session of an older year link, else null (no filter).</summary>
    public async Task<int?> PickAsync(int? sessionId, int? year) =>
        sessionId ?? (year is int ? (await ForYearAsync(year))?.Id ?? -1 : null);
}

public sealed class PartService(AppDbContext db, SessionLookup sessions, IStockNotifier? notify = null)
{
    public IQueryable<InventoryPart> Scoped(AdminScope s)
    {
        var q = db.InventoryParts.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => x.WarehouseId != null && ids.Contains(x.WarehouseId.Value)); }
        var branches = s.Branches.ToList();
        return q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value));
    }

    public async Task<(PagedResult<PartRow> page, StocktakingSession? session)> ListAsync(PageQuery p, AdminScope s, string? view = null, int? sessionId = null)
    {
        var latest = await sessions.ForSessionAsync(sessionId);
        var sid = latest?.Id ?? -1;
        var q = Scoped(s).Where(x => x.SessionId == sid && !db.NewPartRequests.Any(r => r.InventoryPartId == x.Id));
        if (view == "counted") q = q.Where(x => x.CountedQuantity != null);
        else if (view == "diff") q = q.Where(x => x.CountedQuantity != null && x.CountedQuantity != x.SystemQuantity);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t));
        var mask = s.HidesStockFigures;
        var rows = q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id).Select(x => new PartRow(x.Id, x.PartNumber, x.Description, x.CountedQuantity, mask ? 0 : x.SystemQuantity,
            x.Branch != null ? x.Branch.Name : null, x.Warehouse != null ? x.Warehouse.Name : null, x.CountedByUser != null ? x.CountedByUser.Name : null, x.CountedByUserId,
            mask ? (x.CountedQuantity == null ? "—" : x.CountedQuantity > x.SystemQuantity ? "أعلى" : x.CountedQuantity < x.SystemQuantity ? "أقل" : "مطابق") : null));
        return (Paging.Build(rows, p), latest);
    }

    public Task<PagedResult<RecountListRow>> RecountsAsync(PageQuery p, AdminScope s, int? sessionId = null)
    {
        var q = db.RecountRequests.AsNoTracking().AsQueryable();
        if (sessionId is int y) q = q.Where(x => x.InventoryPart!.SessionId == y);
        if (!s.IsUnrestricted)
        {
            if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); q = q.Where(x => x.InventoryPart!.WarehouseId != null && ids.Contains(x.InventoryPart.WarehouseId.Value)); }
            else { var branches = s.Branches.ToList(); q = q.Where(x => x.InventoryPart!.BranchId != null && branches.Contains(x.InventoryPart.BranchId.Value)); }
        }
        if (p.Term is string t) q = q.Where(x => x.InventoryPart!.PartNumber.Contains(t) || x.RequestedByAdmin!.Name.Contains(t) || x.AssignedUser!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new RecountListRow(x.Id, x.InventoryPart!.PartNumber, x.RequestedByAdmin!.Name, x.AssignedUser!.Name, x.CreatedAt, x.Status, x.ResultQuantity));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public async Task<PartDetails?> GetAsync(int id, AdminScope s)
    {
        var part = await Scoped(s).Include(x => x.Branch).Include(x => x.Warehouse).Include(x => x.CountedByUser).Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == id);
        if (part == null) return null;
        var rec = await db.RecountRequests.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new RecountRow(x.Id, x.AssignedUser!.Name, x.RequestedByAdmin!.Name, x.Status, x.ResultQuantity, x.CreatedAt, x.CompletedAt)).ToListAsync();
        var logs = await db.PartChangeLogs.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new ChangeLogRow(x.Id, x.Field, x.OldValue, x.NewValue, x.ChangedBy, x.Source, x.CreatedAt)).ToListAsync();
        string? indicator = null;
        if (s.HidesStockFigures)
        {
            indicator = Labels.Direction(part.CountedQuantity, part.SystemQuantity);
            part.SystemQuantity = 0;
            part.Price = 0;
        }
        return new PartDetails { Part = part, Recounts = rec, Logs = logs, Indicator = indicator };
    }

    /// <summary>
    /// Active counters who may count this part. A warehouse-pinned part accepts only users linked to that warehouse.
    /// A branch-level part (imported with a branch and no warehouse yet) accepts users linked to a warehouse of that branch.
    /// </summary>
    public async Task<List<NamedItem>> CandidateUsersAsync(int partId)
    {
        var part = await db.InventoryParts.AsNoTracking().Where(p => p.Id == partId).Select(p => new { p.WarehouseId, p.BranchId }).FirstOrDefaultAsync();
        if (part == null) return new();
        return await CountersFor(part.WarehouseId, part.BranchId).OrderBy(u => u.Name).ThenBy(u => u.Id).Select(u => new NamedItem(u.Id, u.Name, null)).ToListAsync();
    }

    /// <summary>Same eligibility rule as <see cref="CandidateUsersAsync"/>, so a posted user id cannot bypass the list.</summary>
    private IQueryable<AppUser> CountersFor(int? warehouseId, int? branchId)
    {
        var links = db.AppUserWarehouses.AsNoTracking().Where(w => w.AppUser!.IsActive);
        if (warehouseId is int wid) links = links.Where(w => w.WarehouseId == wid);
        else if (branchId is int bid) links = links.Where(w => w.Warehouse!.BranchId == bid);
        else return db.AppUsers.AsNoTracking().Where(u => u.Id == 0);
        var ids = links.Select(w => w.AppUserId).Distinct();
        return db.AppUsers.AsNoTracking().Where(u => u.IsActive && ids.Contains(u.Id));
    }

    public async Task<OpResult> RequestRecountAsync(int partId, int? userId, AdminScope s)
    {
        var part = await Scoped(s).Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == partId);
        if (part == null) return OpResult.Fail("القطعة غير موجودة");
        if (!s.CanRequestRecount) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        if (part.Session!.Status != SessionStatus.InProgress) return OpResult.Fail("جلسة الجرد منتهية");
        var target = userId ?? part.CountedByUserId;
        if (target == null) return OpResult.Fail("لم تُعدّ القطعة بعد، اختر مستخدمًا");
        var ok = await CountersFor(part.WarehouseId, part.BranchId).AnyAsync(u => u.Id == target);
        if (!ok) return OpResult.Fail("المستخدم غير مرتبط بمخزن القطعة أو معطّل");
        if (await db.RecountRequests.AnyAsync(x => x.InventoryPartId == partId && x.AssignedUserId == target && x.Status == RecountStatus.Pending))
            return OpResult.Fail("يوجد طلب إعادة عد مفتوح لنفس المستخدم");
        var rec = new RecountRequest { InventoryPartId = partId, AssignedUserId = target.Value, RequestedByAdminId = s.Id };
        db.RecountRequests.Add(rec);
        await db.SaveChangesAsync();
        if (notify != null)
        {
            try { await notify.RecountAssignedAsync(rec.AssignedUserId, rec.Id, part.PartNumber); }
            catch { /* delivery never rolls back the request */ }
        }
        return OpResult.Success();
    }
}

public sealed record RecountAskRow(int Id, string PartNumber, string Description, string RequestedBy, string? BranchName, string? WarehouseName, string? CountedLocation, int? CountedQuantity, RecountAskStatus Status, string? DecidedBy, DateTime CreatedAt, string SessionName);

/// <summary>Counters ask to recount a part they already counted. Approval hands the part back to that counter as a recount task and notifies them.</summary>
public sealed class RecountAskService(AppDbContext db, IStockNotifier? notify = null)
{
    private IQueryable<RecountAsk> Scoped(AdminScope s)
    {
        var q = db.RecountAsks.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => x.InventoryPart!.WarehouseId != null && ids.Contains(x.InventoryPart.WarehouseId.Value)); }
        var branches = s.Branches.ToList();
        return q.Where(x => x.InventoryPart!.BranchId != null && branches.Contains(x.InventoryPart.BranchId.Value));
    }

    public Task<PagedResult<RecountAskRow>> ListAsync(PageQuery p, AdminScope s, int? sessionId = null)
    {
        var q = Scoped(s);
        if (sessionId is int y) q = q.Where(x => x.InventoryPart!.SessionId == y);
        if (p.Term is string t) q = q.Where(x => x.InventoryPart!.PartNumber.Contains(t) || x.RequestedByUser!.Name.Contains(t));
        var rows = q.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new RecountAskRow(x.Id, x.InventoryPart!.PartNumber, x.InventoryPart.Description, x.RequestedByUser!.Name,
                x.InventoryPart.Branch != null ? x.InventoryPart.Branch.Name : null, x.InventoryPart.Warehouse != null ? x.InventoryPart.Warehouse.Name : null,
                x.InventoryPart.CountedLocation, x.InventoryPart.CountedQuantity, x.Status, x.DecidedByAdmin != null ? x.DecidedByAdmin.Name : null, x.CreatedAt, x.InventoryPart.Session!.Name));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public async Task<OpResult> ApproveAsync(int id, AdminScope s)
    {
        if (!s.CanRequestRecount) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        var ask = await Scoped(s).Include(x => x.InventoryPart).ThenInclude(p => p!.Session).FirstOrDefaultAsync(x => x.Id == id);
        if (ask == null) return OpResult.Fail("الطلب غير موجود");
        if (ask.Status != RecountAskStatus.Pending) return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً");
        var part = ask.InventoryPart!;
        if (part.Session!.Status != SessionStatus.InProgress) return OpResult.Fail("جلسة الجرد منتهية");
        if (await db.RecountRequests.AnyAsync(x => x.InventoryPartId == part.Id && x.AssignedUserId == ask.RequestedByUserId && x.Status == RecountStatus.Pending))
            return OpResult.Fail("يوجد طلب إعادة عد مفتوح لنفس المستخدم");
        await using var tx = await db.Database.BeginTransactionAsync();
        var now = DateTime.UtcNow;
        var n = await db.RecountAsks.Where(x => x.Id == id && x.Status == RecountAskStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecountAskStatus.Approved).SetProperty(x => x.DecidedByAdminId, s.Id).SetProperty(x => x.DecidedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً"); }
        var rec = new RecountRequest { InventoryPartId = part.Id, AssignedUserId = ask.RequestedByUserId, RequestedByAdminId = s.Id };
        db.RecountRequests.Add(rec);
        await db.SaveChangesAsync(); await tx.CommitAsync();
        if (notify != null)
        {
            try { await notify.RecountAssignedAsync(rec.AssignedUserId, rec.Id, part.PartNumber); }
            catch { /* delivery never rolls back the approval */ }
        }
        return OpResult.Success(rec.Id);
    }

    public async Task<OpResult> RejectAsync(int id, string? reason, AdminScope s)
    {
        if (!s.CanRequestRecount) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        var why = reason?.Trim();
        if (string.IsNullOrEmpty(why)) return OpResult.Fail("السبب مطلوب");
        if (why.Length > 1000) why = why[..1000];
        if (Validators.HasMarkup(why)) return OpResult.Fail(Validators.MarkupError);
        var ask = await Scoped(s).FirstOrDefaultAsync(x => x.Id == id);
        if (ask == null) return OpResult.Fail("الطلب غير موجود");
        var now = DateTime.UtcNow;
        var n = await db.RecountAsks.Where(x => x.Id == id && x.Status == RecountAskStatus.Pending)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RecountAskStatus.Rejected).SetProperty(x => x.DecidedByAdminId, s.Id).SetProperty(x => x.DecidedAt, now).SetProperty(x => x.DecisionReason, why));
        return n == 1 ? OpResult.Success() : OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً");
    }
}

public sealed class VinService(AppDbContext db, SessionLookup sessions)
{
    public IQueryable<ScannedVin> Scoped(AdminScope s)
    {
        var q = db.ScannedVins.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => ids.Contains(x.WarehouseId)); }
        var branches = s.Branches.ToList();
        return q.Where(x => branches.Contains(x.BranchId));
    }

    public Task<PagedResult<VinRow>> ListAsync(PageQuery p, AdminScope s, int? sessionId = null)
    {
        var q = Scoped(s);
        if (sessionId is int y) q = q.Where(x => x.SessionId == y);
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t) || x.User!.Name.Contains(t) || x.Warehouse!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new VinRow(x.Id, x.Vin, x.Session!.Name, x.User!.Name, x.Warehouse!.Name, x.Branch!.Name, x.Latitude, x.Longitude, x.CreatedAt,
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.Model).FirstOrDefault(),
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.ManufactureYear).FirstOrDefault()));
        return Task.FromResult(Paging.Build(rows, p));
    }

    /// <summary>Vehicle differences: VINs of the session file that nobody has scanned yet.</summary>
    public async Task<PagedResult<MissingVinRow>> MissingAsync(PageQuery p, AdminScope s, int? sessionId = null)
    {
        var session = await sessions.ForSessionAsync(sessionId);
        var sid = session?.Id ?? -1;
        var q = db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sid && !db.ScannedVins.Any(v => v.SessionId == x.SessionId && v.Vin == x.Vin));
        if (!s.IsUnrestricted)
        {
            if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); q = q.Where(x => x.WarehouseId != null && ids.Contains(x.WarehouseId.Value)); }
            else { var branches = s.Branches.ToList(); q = q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value)); }
        }
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t) || (x.Model != null && x.Model.Contains(t)));
        var rows = q.OrderBy(x => x.Vin).Select(x => new MissingVinRow(x.Id, x.Vin, x.Model, x.ManufactureYear, x.Warehouse != null ? x.Warehouse.Name : null, x.Branch != null ? x.Branch.Name : null, session!.Name, x.CreatedAt));
        return Paging.Build(rows, p);
    }

    public Task<ScannedVin?> GetAsync(int id, AdminScope s) => Scoped(s).Include(x => x.User).Include(x => x.Session).Include(x => x.Branch).Include(x => x.Warehouse).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<Dictionary<string, string>> UpdateAsync(int id, string? vin, int warehouseId, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        var v = Validators.NormalizeVin(vin);
        if (!Validators.IsVin(v)) err["Vin"] = Validators.VinError;
        var wh = await db.Warehouses.FirstOrDefaultAsync(x => x.Id == warehouseId && (s.ScopeBranch == null || x.BranchId == s.ScopeBranch));
        if (wh == null) err["WarehouseId"] = "اختر المستودع";
        var e = await db.ScannedVins.FirstOrDefaultAsync(x => x.Id == id && (s.ScopeBranch == null || x.BranchId == s.ScopeBranch));
        if (e == null) err[""] = "المركبة غير موجودة";
        else if (await db.ScannedVins.AnyAsync(x => x.SessionId == e.SessionId && x.Vin == v && x.Id != id)) err["Vin"] = "رقم VIN مسجل في هذه الجلسة";
        if (err.Count > 0) return err;
        e!.Vin = v; e.WarehouseId = wh!.Id; e.BranchId = wh.BranchId;
        await db.SaveChangesAsync();
        return err;
    }
}

public sealed class VinRequestService(AppDbContext db)
{
    private IQueryable<VinCorrectionRequest> Scoped(AdminScope s)
    {
        var q = db.VinCorrections.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => ids.Contains(x.ScannedVin!.WarehouseId)); }
        var branches = s.Branches.ToList();
        return q.Where(x => branches.Contains(x.ScannedVin!.BranchId));
    }

    public Task<PagedResult<VinReqRow>> ListAsync(PageQuery p, AdminScope s, int? sessionId = null)
    {
        var q = Scoped(s);
        if (sessionId is int y) q = q.Where(x => x.ScannedVin!.SessionId == y);
        if (p.Term is string t) q = q.Where(x => x.CurrentVin.Contains(t) || x.NewVin.Contains(t) || x.RequestedByUser!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new VinReqRow(x.Id, x.CurrentVin, x.NewVin, x.Reason, x.RequestedByUser!.Name,
            x.ApprovedByAdmin != null ? x.ApprovedByAdmin.Name : null, x.Status, x.CreatedAt, x.ApprovedAt));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public async Task<OpResult> DecideAsync(int id, bool approve, AdminScope s)
    {
        if (s.Role is not (AdminRole.Admin or AdminRole.WarehouseManager or AdminRole.VehicleWarehouseManager)) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        var r = await db.VinCorrections.Include(x => x.ScannedVin).FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !s.AllowsInventory(r.ScannedVin!.WarehouseId, r.ScannedVin.BranchId)) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != RequestStatus.Pending) return OpResult.Fail("الطلب ليس قيد الانتظار");
        if (approve && await db.ScannedVins.AnyAsync(x => x.SessionId == r.ScannedVin!.SessionId && x.Vin == r.NewVin && x.Id != r.ScannedVinId))
            return OpResult.Fail("رقم VIN الجديد مسجل لمركبة أخرى في الجلسة نفسها");
        r.Status = approve ? RequestStatus.Approved : RequestStatus.Rejected;
        r.ApprovedByAdminId = s.Id; r.ApprovedAt = DateTime.UtcNow;
        if (approve) r.ScannedVin!.Vin = r.NewVin;
        await db.SaveChangesAsync();
        return OpResult.Success();
    }
}
