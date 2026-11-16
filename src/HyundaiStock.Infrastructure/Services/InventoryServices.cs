using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HyundaiStock.Infrastructure.Services;

public sealed record PartRow(int Id, string PartNumber, string Description, int? CountedQuantity, int SystemQuantity, string? BranchName, string? WarehouseName, string? UserName, int? CountedByUserId, string? Indicator = null);
public sealed record RecountListRow(int Id, string PartNumber, string RequestedBy, string AssignedTo, DateTime CreatedAt, RecountStatus Status, int? ResultQuantity);
public sealed record ChangeLogRow(int Id, string Field, string? OldValue, string? NewValue, string ChangedBy, string Source, DateTime CreatedAt);
public sealed record RecountRow(int Id, string UserName, string RequestedBy, RecountStatus Status, int? ResultQuantity, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record QtyRow(int Id, string PartNumber, string PartDescription, int? OldQuantity, int NewQuantity, string Reason, string RequestedBy, ApprovalLevel Level, RequestStatus Status, DateTime CreatedAt, int? InventoryPartId, string SentTo = "");
public sealed record VinRow(int Id, string Vin, int SessionYear, string UserName, string WarehouseName, string BranchName, double? Latitude, double? Longitude, DateTime CreatedAt, string? VehicleType = null, int? ManufactureYear = null);
public sealed record MissingVinRow(int Id, string Vin, string? VehicleType, int? ManufactureYear, string? WarehouseName, string? BranchName, int SessionYear, DateTime CreatedAt);
public sealed record VinReqRow(int Id, string CurrentVin, string NewVin, string Reason, string RequestedBy, string? ApprovedBy, RequestStatus Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record SessionRow(int Id, int Year, SessionStatus Status, string StartedBy, DateTime StartedAt, DateTime? EndedAt, string FileName, int Parts, int Vins);

public sealed class PartDetails
{
    public InventoryPart Part { get; init; } = null!;
    public List<QtyRow> Corrections { get; init; } = new();
    public List<RecountRow> Recounts { get; init; } = new();
    public List<ChangeLogRow> Logs { get; init; } = new();
    public string? Indicator { get; init; }
}

public sealed class QtyInput
{
    public string? PartNumber { get; set; }
    public string? PartDescription { get; set; }
    public int? OldQuantity { get; set; }
    public int? NewQuantity { get; set; }
    public string? Reason { get; set; }
    public int? InventoryPartId { get; set; }
    /// <summary>user, branch, or warehouse. Empty on an edit that keeps the previous audience.</summary>
    public string? Audience { get; set; }
    public int? AudienceUserId { get; set; }
    public int? AudienceBranchId { get; set; }
    public int? AudienceWarehouseId { get; set; }
}

public sealed record AudienceChoices(List<NamedItem> Users, List<NamedItem> Branches, List<NamedItem> Warehouses);

public sealed class SessionLookup(AppDbContext db)
{
    public Task<StocktakingSession?> CurrentAsync() => db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.InProgress).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
    public Task<StocktakingSession?> LatestAsync() => db.Sessions.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
    public Task<List<NamedItem>> OptionsAsync() => db.Sessions.AsNoTracking().OrderByDescending(x => x.StartedAt).Select(x => new NamedItem(x.Id, x.Year.ToString(), null)).ToListAsync();
    /// <summary>Stocktaking years that have a session, newest first. Feeds the year filter on every page.</summary>
    public Task<List<int>> YearsAsync() => db.Sessions.AsNoTracking().Select(x => x.Year).Distinct().OrderByDescending(x => x).ToListAsync();
    /// <summary>The session of the given year (one per year), or the latest session when no year is chosen. A year with no session returns null.</summary>
    public async Task<StocktakingSession?> ForYearAsync(int? year) => year is int y
        ? await db.Sessions.AsNoTracking().Where(x => x.Year == y).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync()
        : await LatestAsync();
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

    public async Task<(PagedResult<PartRow> page, StocktakingSession? session)> ListAsync(PageQuery p, AdminScope s, string? view = null, int? year = null)
    {
        var latest = await sessions.ForYearAsync(year);
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

    public Task<PagedResult<RecountListRow>> RecountsAsync(PageQuery p, AdminScope s, int? year = null)
    {
        var q = db.RecountRequests.AsNoTracking().AsQueryable();
        if (year is int y) q = q.Where(x => x.InventoryPart!.Session!.Year == y);
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
        var corr = await db.QuantityCorrections.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new QtyRow(x.Id, x.PartNumber, x.PartDescription, x.OldQuantity, x.NewQuantity, x.Reason, x.RequestedByAdmin != null ? x.RequestedByAdmin.Name : x.RequestedByUser != null ? x.RequestedByUser.Name + " (التطبيق)" : "", x.ApprovalLevel, x.Status, x.CreatedAt, x.InventoryPartId, x.AudienceKind == 1 ? (x.AudienceUser != null ? x.AudienceUser.Name : "—") : x.AudienceKind == 2 ? "مستخدمو الفرع" : x.AudienceKind == 3 ? "مستخدمو المخزن" : "—")).ToListAsync();
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
        return new PartDetails { Part = part, Corrections = corr, Recounts = rec, Logs = logs, Indicator = indicator };
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

public sealed class QuantityRequestService(AppDbContext db, SessionLookup sessions, ILogger<QuantityRequestService>? logger = null, IStockNotifier? notify = null)
{
    private IQueryable<QuantityCorrectionRequest> Scoped(AdminScope s)
    {
        var q = db.QuantityCorrections.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => x.WarehouseId != null && ids.Contains(x.WarehouseId.Value)); }
        var branches = s.Branches.ToList();
        return q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value));
    }

    private static bool InScope(AdminScope s, int? branchId, int? warehouseId) => s.AllowsInventory(warehouseId, branchId);

    public Task<PagedResult<QtyRow>> ListAsync(PageQuery p, AdminScope s, int? year = null)
    {
        var q = Scoped(s);
        if (year is int y) q = q.Where(x => x.InventoryPart != null && x.InventoryPart.Session!.Year == y);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t) || x.PartDescription.Contains(t) || (x.RequestedByAdmin != null && x.RequestedByAdmin.Name.Contains(t)) || (x.RequestedByUser != null && x.RequestedByUser.Name.Contains(t)));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new QtyRow(x.Id, x.PartNumber, x.PartDescription, x.OldQuantity, x.NewQuantity, x.Reason, x.RequestedByAdmin != null ? x.RequestedByAdmin.Name : x.RequestedByUser != null ? x.RequestedByUser.Name + " (التطبيق)" : "", x.ApprovalLevel, x.Status, x.CreatedAt, x.InventoryPartId,
            x.AudienceKind == 1 ? (x.AudienceUser != null ? x.AudienceUser.Name : "—") : x.AudienceKind == 2 ? "مستخدمو فرع " + (db.Branches.Where(b => b.Id == x.BranchId).Select(b => b.Name).FirstOrDefault() ?? "") : x.AudienceKind == 3 ? (x.Warehouse != null ? "مستخدمو مخزن " + x.Warehouse.Name : "مستخدمو المخزن") : "—"));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public Task<QuantityCorrectionRequest?> GetAsync(int id, AdminScope s) => Scoped(s).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<Dictionary<string, string>> SaveAsync(int? id, QtyInput i, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        if (s.HidesStockFigures || s.Role == AdminRole.VehicleWarehouseManager) { err[""] = "ليس لديك صلاحية لتنفيذ هذه العملية"; return err; }
        var pn = i.PartNumber?.Trim(); var desc = i.PartDescription?.Trim(); var reason = i.Reason?.Trim();
        QuantityCorrectionRequest? existing = null;
        if (id is int editId)
        {
            existing = await db.QuantityCorrections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == editId);
            if (existing == null || !InScope(s, existing.BranchId, existing.WarehouseId)) { err[""] = "الطلب غير موجود"; return err; }
            if (existing.Status != RequestStatus.Pending || existing.ApprovalLevel != ApprovalLevel.None) { err[""] = "لا يمكن تعديل طلب بعد بدء الموافقة عليه"; return err; }
            // Preserve the linked row when the same part number exists in multiple warehouses.
            if (existing.InventoryPartId is int linked)
            {
                i.InventoryPartId = linked;
                if (!string.IsNullOrEmpty(pn) && pn != existing.PartNumber) { err["PartNumber"] = "لا يمكن تغيير قطعة الطلب"; return err; }
            }
        }
        IQueryable<InventoryPart> ScopedParts()
        {
            var q = db.InventoryParts.AsQueryable();
            if (s.IsUnrestricted) return q;
            if (s.Role == AdminRole.WarehouseManager) { var whs = s.Warehouses.ToList(); return q.Where(x => x.WarehouseId != null && whs.Contains(x.WarehouseId.Value)); }
            var branches = s.Branches.ToList(); return q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value));
        }
        InventoryPart? part = null;
        if (i.InventoryPartId is int pid)
        {
            var pq = ScopedParts();
            part = await pq.FirstOrDefaultAsync(x => x.Id == pid);
            if (part == null) err[""] = "القطعة غير موجودة";
            else { pn ??= part.PartNumber; desc ??= part.Description; i.OldQuantity ??= part.CountedQuantity ?? part.SystemQuantity; }
        }
        if (part == null && !string.IsNullOrEmpty(pn))
        {
            var cur0 = await sessions.CurrentAsync();
            if (cur0 != null)
            {
                var pq0 = ScopedParts().Where(x => x.SessionId == cur0.Id && x.PartNumber == pn);
                part = await pq0.OrderBy(x => x.Id).FirstOrDefaultAsync();
                if (part != null) { if (string.IsNullOrEmpty(desc)) desc = part.Description; i.OldQuantity ??= part.CountedQuantity ?? part.SystemQuantity; }
            }
        }
        if (string.IsNullOrEmpty(pn)) err["PartNumber"] = "رقم القطعة مطلوب";
        if (string.IsNullOrEmpty(desc)) err["PartDescription"] = "وصف القطعة مطلوب";
        if (i.NewQuantity is null) err["NewQuantity"] = "الكمية الجديدة مطلوبة";
        else if (i.NewQuantity < 0) err["NewQuantity"] = "يجب ألا تكون الكمية سالبة";
        if (i.OldQuantity < 0) err["OldQuantity"] = "يجب ألا تكون الكمية سالبة";
        if (err.Count > 0) return err;

        if (part == null)
        {
            var cur = await sessions.CurrentAsync();
            if (cur != null)
            {
                var pq = ScopedParts().Where(x => x.SessionId == cur.Id && x.PartNumber == pn);
                part = await pq.OrderBy(x => x.Id).FirstOrDefaultAsync();
            }
        }

        int? audienceKind = null;
        int? audienceUser = null;
        List<int>? recipients = null;
        if (!string.IsNullOrWhiteSpace(i.Audience))
        {
            var audience = await ResolveAudienceAsync(i, part, s);
            if (audience.Error != null) { err[""] = audience.Error; return err; }
            audienceKind = audience.Kind;
            audienceUser = audience.UserId;
            recipients = audience.Recipients;
        }
        else if (id == null)
        {
            err[""] = "اختر مستخدمًا أو فرعًا أو مخزنًا";
            return err;
        }

        QuantityCorrectionRequest? r;
        if (id is int rid)
        {
            r = existing;
            if (r == null || !InScope(s, r.BranchId, r.WarehouseId)) { err[""] = "الطلب غير موجود"; return err; }
            if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.None) { err[""] = "لا يمكن تعديل طلب بعد بدء الموافقة عليه"; return err; }
        }
        else
        {
            r = new QuantityCorrectionRequest { RequestedByAdminId = s.Id };
            db.QuantityCorrections.Add(r);
        }
        if (part != null && !await db.Sessions.AnyAsync(x => x.Id == part.SessionId && x.Status == SessionStatus.InProgress)) { err[""] = "جلسة الجرد غير متاحة"; return err; }
        r.PartNumber = pn!; r.PartDescription = desc!; r.OldQuantity = i.OldQuantity; r.NewQuantity = i.NewQuantity!.Value; r.Reason = reason ?? "";
        r.InventoryPartId = part?.Id ?? r.InventoryPartId;
        r.BranchId = part?.BranchId ?? r.BranchId ?? s.BranchId;
        r.WarehouseId = part?.WarehouseId ?? r.WarehouseId;
        if (audienceKind is int kind)
        {
            r.AudienceKind = kind;
            r.AudienceUserId = audienceUser;
        }
        r.UpdatedAt = DateTime.UtcNow;
        if (id is int updateId)
        {
            var changed = await db.QuantityCorrections.Where(x => x.Id == updateId && x.Status == RequestStatus.Pending && x.ApprovalLevel == ApprovalLevel.None)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.PartNumber, r.PartNumber).SetProperty(x => x.PartDescription, r.PartDescription)
                    .SetProperty(x => x.OldQuantity, r.OldQuantity).SetProperty(x => x.NewQuantity, r.NewQuantity).SetProperty(x => x.Reason, r.Reason)
                    .SetProperty(x => x.InventoryPartId, r.InventoryPartId).SetProperty(x => x.BranchId, r.BranchId).SetProperty(x => x.WarehouseId, r.WarehouseId)
                    .SetProperty(x => x.AudienceKind, r.AudienceKind).SetProperty(x => x.AudienceUserId, r.AudienceUserId)
                    .SetProperty(x => x.UpdatedAt, r.UpdatedAt));
            if (changed != 1) err[""] = "لا يمكن تعديل طلب بعد بدء الموافقة عليه";
        }
        else await db.SaveChangesAsync();
        if (err.Count == 0 && recipients is { Count: > 0 } && notify != null)
        {
            try { await notify.QuantityRequestedAsync(recipients, id ?? r.Id, r.PartNumber); }
            catch { /* delivery never rolls back the request */ }
        }
        return err;
    }

    public async Task<AudienceChoices> ChoicesAsync(AdminScope s)
    {
        IQueryable<Branch> branches = db.Branches.AsNoTracking();
        IQueryable<Warehouse> warehouses = db.Warehouses.AsNoTracking();
        if (!s.IsUnrestricted)
        {
            if (s.Role == AdminRole.WarehouseManager)
            {
                var ids = s.Warehouses.ToList();
                warehouses = warehouses.Where(x => ids.Contains(x.Id));
                branches = branches.Where(x => x.Warehouses.Any(w => ids.Contains(w.Id)));
            }
            else
            {
                var ids = s.Branches.ToList();
                branches = branches.Where(x => ids.Contains(x.Id));
                warehouses = warehouses.Where(x => ids.Contains(x.BranchId));
            }
        }
        var branchItems = await branches.OrderBy(x => x.Name).Select(x => new NamedItem(x.Id, x.Name, null)).ToListAsync();
        var warehouseItems = await warehouses.OrderBy(x => x.Name).Select(x => new NamedItem(x.Id, x.Name, x.BranchId)).ToListAsync();
        var warehouseIds = warehouseItems.Select(x => x.Id).ToList();
        var branchIds = branchItems.Select(x => x.Id).ToList();
        var users = await db.AppUsers.AsNoTracking().Where(u => u.IsActive && (u.Warehouses.Any(w => warehouseIds.Contains(w.WarehouseId)) || u.Branches.Any(b => branchIds.Contains(b.BranchId))))
            .OrderBy(u => u.Name).Select(u => new NamedItem(u.Id, u.Name, null)).ToListAsync();
        return new AudienceChoices(users, branchItems, warehouseItems);
    }

    private async Task<(int Kind, int? UserId, List<int> Recipients, string? Error)> ResolveAudienceAsync(QtyInput i, InventoryPart? part, AdminScope s)
    {
        var kind = i.Audience?.Trim().ToLowerInvariant();
        if (kind == "user")
        {
            if (i.AudienceUserId is not int uid || uid <= 0) return (0, null, new(), "اختر المستخدم");
            var pool = AudienceUsers(part?.WarehouseId, part?.BranchId);
            if (part == null) pool = AudienceUsers(i.AudienceWarehouseId, i.AudienceBranchId);
            if (!await pool.AnyAsync(u => u.Id == uid)) return (0, null, new(), "المستخدم غير مرتبط بهذا المخزن أو الفرع");
            if (!await CanAddressAsync(s, part?.BranchId ?? i.AudienceBranchId, part?.WarehouseId ?? i.AudienceWarehouseId)) return (0, null, new(), "ليس لديك صلاحية لتنفيذ هذه العملية");
            return (1, uid, new List<int> { uid }, null);
        }
        if (kind == "branch")
        {
            var bid = part?.BranchId ?? i.AudienceBranchId;
            if (bid is not int branchId) return (0, null, new(), "اختر الفرع");
            if (part?.BranchId is int partBranch && i.AudienceBranchId is int posted && posted != partBranch) return (0, null, new(), "الفرع لا يطابق فرع القطعة");
            if (!await CanAddressAsync(s, branchId, null)) return (0, null, new(), "ليس لديك صلاحية لتنفيذ هذه العملية");
            var ids = await UsersOfBranch(branchId).Select(u => u.Id).Distinct().ToListAsync();
            if (ids.Count == 0) return (0, null, new(), "لا يوجد مستخدمون نشطون مرتبطون بهذا الفرع");
            return (2, null, ids, null);
        }
        if (kind == "warehouse")
        {
            var wid = part?.WarehouseId ?? i.AudienceWarehouseId;
            if (wid is not int warehouseId) return (0, null, new(), "اختر المخزن");
            if (part?.WarehouseId is int partWarehouse && i.AudienceWarehouseId is int posted && posted != partWarehouse) return (0, null, new(), "المخزن لا يطابق مخزن القطعة");
            if (!await CanAddressAsync(s, null, warehouseId)) return (0, null, new(), "ليس لديك صلاحية لتنفيذ هذه العملية");
            var ids = await UsersOfWarehouse(warehouseId).Select(u => u.Id).Distinct().ToListAsync();
            if (ids.Count == 0) return (0, null, new(), "لا يوجد مستخدمون نشطون مرتبطون بهذا المخزن");
            return (3, null, ids, null);
        }
        return (0, null, new(), "اختر مستخدمًا أو فرعًا أو مخزنًا");
    }

    private IQueryable<AppUser> AudienceUsers(int? warehouseId, int? branchId)
    {
        if (warehouseId is int wid) return UsersOfWarehouse(wid);
        if (branchId is int bid) return UsersOfBranch(bid);
        return db.AppUsers.AsNoTracking().Where(u => false);
    }

    private IQueryable<AppUser> UsersOfWarehouse(int warehouseId) =>
        db.AppUsers.AsNoTracking().Where(u => u.IsActive && u.Warehouses.Any(w => w.WarehouseId == warehouseId));

    private IQueryable<AppUser> UsersOfBranch(int branchId) =>
        db.AppUsers.AsNoTracking().Where(u => u.IsActive && (u.Branches.Any(b => b.BranchId == branchId) || u.Warehouses.Any(w => w.Warehouse!.BranchId == branchId)));

    private async Task<bool> CanAddressAsync(AdminScope s, int? branchId, int? warehouseId)
    {
        if (s.IsUnrestricted) return true;
        if (s.Role == AdminRole.WarehouseManager)
        {
            var ids = s.Warehouses.ToList();
            if (warehouseId is int wid) return ids.Contains(wid);
            if (branchId is int bid) return await db.Warehouses.AnyAsync(x => x.BranchId == bid && ids.Contains(x.Id));
            return false;
        }
        var branches = s.Branches.ToList();
        if (branchId is int b) return branches.Contains(b);
        if (warehouseId is int w) return await db.Warehouses.AnyAsync(x => x.Id == w && branches.Contains(x.BranchId));
        return false;
    }

    public Task<OpResult> ApproveAsync(int id, AdminScope s) => FirstApproveAsync(id, s);

    public async Task<OpResult> FirstApproveAsync(int id, AdminScope s)
    {
        if (!s.CanFirstApproveQty) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.QuantityCorrections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !InScope(s, r.BranchId, r.WarehouseId)) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.None) return OpResult.Fail("الطلب ليس بانتظار مدير المخزن");
        if (r.InventoryPartId is int pid && !await db.InventoryParts.AnyAsync(x => x.Id == pid && x.Session!.Status == SessionStatus.InProgress)) return OpResult.Fail("جلسة الجرد غير متاحة");
        var now = DateTime.UtcNow;
        var n = await db.QuantityCorrections.Where(x => x.Id == id && x.Status == RequestStatus.Pending && x.ApprovalLevel == ApprovalLevel.None)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ApprovalLevel, ApprovalLevel.First).SetProperty(x => x.FirstApprovedByAdminId, s.Id).SetProperty(x => x.FirstApprovedAt, now).SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً"); }
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = r.Id, Action = AuditActions.FirstApproved, FromStatus = "PendingWarehouseApproval", ToStatus = "PendingAccountantApproval", ActorAdminId = s.Id, ActorRole = s.Role.ToString(), BranchId = r.BranchId, WarehouseId = r.WarehouseId, InventoryPartId = r.InventoryPartId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return OpResult.Success();
    }

    public async Task<OpResult> FinalApproveAsync(int id, AdminScope s)
    {
        var timer = Stopwatch.StartNew();
        try { return await FinalApproveCoreAsync(id, s); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            logger?.LogWarning("Quantity approval {RequestId}: local database is busy", id);
            return OpResult.Fail("قاعدة البيانات مشغولة، حدّث حالة الطلب قبل إعادة المحاولة");
        }
        finally { logger?.LogInformation("Quantity approval {RequestId} completed in {ElapsedMs} ms", id, timer.ElapsedMilliseconds); }
    }

    private async Task<OpResult> FinalApproveCoreAsync(int id, AdminScope s)
    {
        if (!s.CanFinalApproveQty) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        var r = await db.QuantityCorrections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !InScope(s, r.BranchId, r.WarehouseId)) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.First) return OpResult.Fail("الطلب يحتاج موافقة مدير المخزن قبل القبول النهائي");
        // Invalid/terminal requests must not acquire SQLite's single writer lock.
        await using var tx = await db.Database.BeginTransactionAsync();
        InventoryPart? part = null;
        if (r.InventoryPartId is int pid)
        {
            part = await db.InventoryParts.Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == pid);
            if (part?.Session?.Status != SessionStatus.InProgress) return OpResult.Fail("جلسة الجرد غير متاحة");
            if (!InScope(s, part!.BranchId, part.WarehouseId)) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        }
        var now = DateTime.UtcNow;
        var n = await db.QuantityCorrections.Where(x => x.Id == id && x.Status == RequestStatus.Pending && x.ApprovalLevel == ApprovalLevel.First)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.ApprovalLevel, ApprovalLevel.Final).SetProperty(x => x.Status, RequestStatus.Approved).SetProperty(x => x.FinalApprovedByAdminId, s.Id).SetProperty(x => x.FinalApprovedAt, now).SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً"); }
        if (part != null)
        {
            db.PartChangeLogs.Add(new PartChangeLog { InventoryPartId = part.Id, Field = "الكمية المحسوبة", OldValue = part.CountedQuantity?.ToString(), NewValue = r.NewQuantity.ToString(), ChangedBy = s.Name, Source = $"طلب تصحيح #{r.Id}" });
            part.CountedQuantity = r.NewQuantity;
            // A held surplus count on a part nobody had counted: credit the counter who sent it.
            if (part.CountedByUserId == null && r.RequestedByUserId is int uid) { part.CountedByUserId = uid; part.CountedAt = now; }
        }
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = r.Id, Action = AuditActions.FinalApproved, FromStatus = "PendingAccountantApproval", ToStatus = "Approved", ActorAdminId = s.Id, ActorRole = s.Role.ToString(), BranchId = r.BranchId, WarehouseId = r.WarehouseId, InventoryPartId = part?.Id ?? r.InventoryPartId });
        try { await db.SaveChangesAsync(); await tx.CommitAsync(); }
        catch (DbUpdateException) { await tx.RollbackAsync(); return OpResult.Fail("تعذر إتمام الموافقة"); }
        return OpResult.Success();
    }

    public async Task<OpResult> RejectAsync(int id, string? reason, AdminScope s)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0) return OpResult.Fail("سبب الرفض مطلوب");
        if (why.Length > 1000) why = why[..1000];
        if (Validators.HasMarkup(why)) return OpResult.Fail(Validators.MarkupError);
        await using var tx = await db.Database.BeginTransactionAsync();
        var r = await db.QuantityCorrections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (r == null || !InScope(s, r.BranchId, r.WarehouseId)) return OpResult.Fail("الطلب غير موجود");
        if (r.Status != RequestStatus.Pending) return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً");
        if (r.ApprovalLevel == ApprovalLevel.None && !s.CanFirstApproveQty) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        if (r.ApprovalLevel == ApprovalLevel.First && !s.CanFinalApproveQty) return OpResult.Fail("الطلب بانتظار القبول النهائي");
        var now = DateTime.UtcNow;
        var n = await db.QuantityCorrections.Where(x => x.Id == id && x.Status == RequestStatus.Pending && x.ApprovalLevel == r.ApprovalLevel)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, RequestStatus.Rejected).SetProperty(x => x.RejectedByAdminId, s.Id).SetProperty(x => x.RejectedAt, now).SetProperty(x => x.DecisionReason, why).SetProperty(x => x.UpdatedAt, now));
        if (n != 1) { await tx.RollbackAsync(); return OpResult.Fail("تم التعامل مع هذا الطلب مسبقاً"); }
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = r.Id, Action = AuditActions.Rejected, FromStatus = r.ApprovalLevel == ApprovalLevel.First ? "PendingAccountantApproval" : "PendingWarehouseApproval", ToStatus = "Rejected", ActorAdminId = s.Id, ActorRole = s.Role.ToString(), Reason = why, BranchId = r.BranchId, WarehouseId = r.WarehouseId, InventoryPartId = r.InventoryPartId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return OpResult.Success();
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

    public Task<PagedResult<VinRow>> ListAsync(PageQuery p, AdminScope s, int? year = null)
    {
        var q = Scoped(s);
        if (year is int y) q = q.Where(x => x.Session!.Year == y);
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t) || x.User!.Name.Contains(t) || x.Warehouse!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new VinRow(x.Id, x.Vin, x.Session!.Year, x.User!.Name, x.Warehouse!.Name, x.Branch!.Name, x.Latitude, x.Longitude, x.CreatedAt,
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.Model).FirstOrDefault(),
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.ManufactureYear).FirstOrDefault()));
        return Task.FromResult(Paging.Build(rows, p));
    }

    /// <summary>Vehicle differences: VINs of the session file that nobody has scanned yet.</summary>
    public async Task<PagedResult<MissingVinRow>> MissingAsync(PageQuery p, AdminScope s, int? year = null)
    {
        var session = await sessions.ForYearAsync(year);
        var sid = session?.Id ?? -1;
        var q = db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sid && !db.ScannedVins.Any(v => v.SessionId == x.SessionId && v.Vin == x.Vin));
        if (!s.IsUnrestricted)
        {
            if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); q = q.Where(x => x.WarehouseId != null && ids.Contains(x.WarehouseId.Value)); }
            else { var branches = s.Branches.ToList(); q = q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value)); }
        }
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t) || (x.Model != null && x.Model.Contains(t)));
        var rows = q.OrderBy(x => x.Vin).Select(x => new MissingVinRow(x.Id, x.Vin, x.Model, x.ManufactureYear, x.Warehouse != null ? x.Warehouse.Name : null, x.Branch != null ? x.Branch.Name : null, session!.Year, x.CreatedAt));
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

    public Task<PagedResult<VinReqRow>> ListAsync(PageQuery p, AdminScope s, int? year = null)
    {
        var q = Scoped(s);
        if (year is int y) q = q.Where(x => x.ScannedVin!.Session!.Year == y);
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
