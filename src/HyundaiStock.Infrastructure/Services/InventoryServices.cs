using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace HyundaiStock.Infrastructure.Services;

public sealed record PartRow(int Id, string PartNumber, string Description, int? CountedQuantity, int SystemQuantity, string? BranchName, string? WarehouseName, string? UserName, int? CountedByUserId);
public sealed record ChangeLogRow(int Id, string Field, string? OldValue, string? NewValue, string ChangedBy, string Source, DateTime CreatedAt);
public sealed record RecountRow(int Id, string UserName, string RequestedBy, RecountStatus Status, int? ResultQuantity, DateTime CreatedAt, DateTime? CompletedAt);
public sealed record QtyRow(int Id, string PartNumber, string PartDescription, int? OldQuantity, int NewQuantity, string Reason, string RequestedBy, ApprovalLevel Level, RequestStatus Status, DateTime CreatedAt, int? InventoryPartId);
public sealed record VinRow(int Id, string Vin, int SessionYear, string UserName, string WarehouseName, string BranchName, double? Latitude, double? Longitude, DateTime CreatedAt);
public sealed record VinReqRow(int Id, string CurrentVin, string NewVin, string Reason, string RequestedBy, string? ApprovedBy, RequestStatus Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record SessionRow(int Id, int Year, SessionStatus Status, string StartedBy, DateTime StartedAt, DateTime? EndedAt, string FileName, int Parts, int Vins);

public sealed class PartDetails
{
    public InventoryPart Part { get; init; } = null!;
    public List<QtyRow> Corrections { get; init; } = new();
    public List<RecountRow> Recounts { get; init; } = new();
    public List<ChangeLogRow> Logs { get; init; } = new();
}

public sealed class QtyInput
{
    public string? PartNumber { get; set; }
    public string? PartDescription { get; set; }
    public int? OldQuantity { get; set; }
    public int? NewQuantity { get; set; }
    public string? Reason { get; set; }
    public int? InventoryPartId { get; set; }
}

public sealed class SessionLookup(AppDbContext db)
{
    public Task<StocktakingSession?> CurrentAsync() => db.Sessions.AsNoTracking().Where(x => x.Status == SessionStatus.InProgress).OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync();
    public Task<StocktakingSession?> LatestAsync() => db.Sessions.AsNoTracking().OrderBy(x => x.Status).ThenByDescending(x => x.StartedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
    public Task<List<NamedItem>> OptionsAsync() => db.Sessions.AsNoTracking().OrderByDescending(x => x.StartedAt).Select(x => new NamedItem(x.Id, x.Year.ToString(), null)).ToListAsync();
}

public sealed class PartService(AppDbContext db, SessionLookup sessions)
{
    public IQueryable<InventoryPart> Scoped(AdminScope s)
    {
        var q = db.InventoryParts.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => x.WarehouseId != null && ids.Contains(x.WarehouseId.Value)); }
        var branches = s.Branches.ToList();
        return q.Where(x => x.BranchId != null && branches.Contains(x.BranchId.Value));
    }

    public async Task<(PagedResult<PartRow> page, StocktakingSession? session)> ListAsync(PageQuery p, AdminScope s)
    {
        var latest = await sessions.LatestAsync();
        var sid = latest?.Id ?? -1;
        var q = Scoped(s).Where(x => x.SessionId == sid);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t));
        var rows = q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id).Select(x => new PartRow(x.Id, x.PartNumber, x.Description, x.CountedQuantity, x.SystemQuantity,
            x.Branch != null ? x.Branch.Name : null, x.Warehouse != null ? x.Warehouse.Name : null, x.CountedByUser != null ? x.CountedByUser.Name : null, x.CountedByUserId));
        return (Paging.Build(rows, p), latest);
    }

    public async Task<PartDetails?> GetAsync(int id, AdminScope s)
    {
        var part = await Scoped(s).Include(x => x.Branch).Include(x => x.Warehouse).Include(x => x.CountedByUser).Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == id);
        if (part == null) return null;
        var corr = await db.QuantityCorrections.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new QtyRow(x.Id, x.PartNumber, x.PartDescription, x.OldQuantity, x.NewQuantity, x.Reason, x.RequestedByAdmin != null ? x.RequestedByAdmin.Name : x.RequestedByUser != null ? x.RequestedByUser.Name + " (التطبيق)" : "", x.ApprovalLevel, x.Status, x.CreatedAt, x.InventoryPartId)).ToListAsync();
        var rec = await db.RecountRequests.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new RecountRow(x.Id, x.AssignedUser!.Name, x.RequestedByAdmin!.Name, x.Status, x.ResultQuantity, x.CreatedAt, x.CompletedAt)).ToListAsync();
        var logs = await db.PartChangeLogs.AsNoTracking().Where(x => x.InventoryPartId == id).OrderByDescending(x => x.CreatedAt)
            .Select(x => new ChangeLogRow(x.Id, x.Field, x.OldValue, x.NewValue, x.ChangedBy, x.Source, x.CreatedAt)).ToListAsync();
        return new PartDetails { Part = part, Corrections = corr, Recounts = rec, Logs = logs };
    }

    /// <summary>App users who can count this part (active, assigned to its warehouse).</summary>
    public Task<List<NamedItem>> CandidateUsersAsync(int partId) =>
        db.InventoryParts.Where(p => p.Id == partId).SelectMany(p => db.AppUserWarehouses.Where(w => w.WarehouseId == p.WarehouseId).Select(w => w.AppUser!))
            .Where(u => u.IsActive).Distinct().OrderBy(u => u.Name).Select(u => new NamedItem(u.Id, u.Name, null)).ToListAsync();

    public async Task<OpResult> RequestRecountAsync(int partId, int? userId, AdminScope s)
    {
        var part = await Scoped(s).Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == partId);
        if (part == null) return OpResult.Fail("القطعة غير موجودة");
        if (s.Role is AdminRole.Accountant or AdminRole.SparePartsManager) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
        if (part.Session!.Status != SessionStatus.InProgress) return OpResult.Fail("جلسة الجرد منتهية");
        var target = userId ?? part.CountedByUserId;
        if (target == null) return OpResult.Fail("لم تُعدّ القطعة بعد، اختر مستخدمًا");
        var ok = await db.AppUsers.AnyAsync(u => u.Id == target && u.IsActive && u.Warehouses.Any(w => w.WarehouseId == part.WarehouseId));
        if (!ok) return OpResult.Fail("المستخدم غير مرتبط بمخزن القطعة أو معطّل");
        if (await db.RecountRequests.AnyAsync(x => x.InventoryPartId == partId && x.AssignedUserId == target && x.Status == RecountStatus.Pending))
            return OpResult.Fail("يوجد طلب إعادة عد مفتوح لنفس المستخدم");
        db.RecountRequests.Add(new RecountRequest { InventoryPartId = partId, AssignedUserId = target.Value, RequestedByAdminId = s.Id });
        await db.SaveChangesAsync();
        return OpResult.Success();
    }
}

public sealed class QuantityRequestService(AppDbContext db, SessionLookup sessions, ILogger<QuantityRequestService>? logger = null)
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

    public Task<PagedResult<QtyRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = Scoped(s);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t) || x.PartDescription.Contains(t) || (x.RequestedByAdmin != null && x.RequestedByAdmin.Name.Contains(t)) || (x.RequestedByUser != null && x.RequestedByUser.Name.Contains(t)));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new QtyRow(x.Id, x.PartNumber, x.PartDescription, x.OldQuantity, x.NewQuantity, x.Reason, x.RequestedByAdmin != null ? x.RequestedByAdmin.Name : x.RequestedByUser != null ? x.RequestedByUser.Name + " (التطبيق)" : "", x.ApprovalLevel, x.Status, x.CreatedAt, x.InventoryPartId));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public Task<QuantityCorrectionRequest?> GetAsync(int id, AdminScope s) => Scoped(s).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<Dictionary<string, string>> SaveAsync(int? id, QtyInput i, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        var pn = i.PartNumber?.Trim(); var desc = i.PartDescription?.Trim(); var reason = i.Reason?.Trim();
        InventoryPart? part = null;
        if (i.InventoryPartId is int pid)
        {
            var pq = db.InventoryParts.AsQueryable(); if (s.ScopeBranch is int sb) pq = pq.Where(x => x.BranchId == sb);
            part = await pq.FirstOrDefaultAsync(x => x.Id == pid);
            if (part == null) err[""] = "القطعة غير موجودة";
            else { pn ??= part.PartNumber; desc ??= part.Description; i.OldQuantity ??= part.CountedQuantity ?? part.SystemQuantity; }
        }
        if (part == null && !string.IsNullOrEmpty(pn))
        {
            var cur0 = await sessions.CurrentAsync();
            if (cur0 != null)
            {
                var pq0 = db.InventoryParts.Where(x => x.SessionId == cur0.Id && x.PartNumber == pn);
                if (s.ScopeBranch is int sb0) pq0 = pq0.Where(x => x.BranchId == sb0);
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
                var pq = db.InventoryParts.Where(x => x.SessionId == cur.Id && x.PartNumber == pn);
                if (s.ScopeBranch is int sb) pq = pq.Where(x => x.BranchId == sb);
                part = await pq.OrderBy(x => x.Id).FirstOrDefaultAsync();
            }
        }

        QuantityCorrectionRequest? r;
        if (id is int rid)
        {
            r = await db.QuantityCorrections.FirstOrDefaultAsync(x => x.Id == rid);
            if (r == null || (s.ScopeBranch is int sb && r.BranchId != sb)) { err[""] = "الطلب غير موجود"; return err; }
            if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.None) { err[""] = "لا يمكن تعديل طلب بعد بدء الموافقة عليه"; return err; }
        }
        else
        {
            r = new QuantityCorrectionRequest { RequestedByAdminId = s.Id };
            db.QuantityCorrections.Add(r);
        }
        r.PartNumber = pn!; r.PartDescription = desc!; r.OldQuantity = i.OldQuantity; r.NewQuantity = i.NewQuantity!.Value; r.Reason = reason ?? "";
        r.InventoryPartId = part?.Id ?? r.InventoryPartId;
        r.BranchId = part?.BranchId ?? r.BranchId ?? s.BranchId;
        r.WarehouseId = part?.WarehouseId ?? r.WarehouseId;
        r.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return err;
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

public sealed class VinService(AppDbContext db)
{
    public IQueryable<ScannedVin> Scoped(AdminScope s)
    {
        var q = db.ScannedVins.AsNoTracking().AsQueryable();
        if (s.IsUnrestricted) return q;
        if (s.Role == AdminRole.WarehouseManager) { var ids = s.Warehouses.ToList(); return q.Where(x => ids.Contains(x.WarehouseId)); }
        var branches = s.Branches.ToList();
        return q.Where(x => branches.Contains(x.BranchId));
    }

    public Task<PagedResult<VinRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = Scoped(s);
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t) || x.User!.Name.Contains(t) || x.Warehouse!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new VinRow(x.Id, x.Vin, x.Session!.Year, x.User!.Name, x.Warehouse!.Name, x.Branch!.Name, x.Latitude, x.Longitude, x.CreatedAt));
        return Task.FromResult(Paging.Build(rows, p));
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

    public Task<PagedResult<VinReqRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = Scoped(s);
        if (p.Term is string t) q = q.Where(x => x.CurrentVin.Contains(t) || x.NewVin.Contains(t) || x.RequestedByUser!.Name.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => new VinReqRow(x.Id, x.CurrentVin, x.NewVin, x.Reason, x.RequestedByUser!.Name,
            x.ApprovedByAdmin != null ? x.ApprovedByAdmin.Name : null, x.Status, x.CreatedAt, x.ApprovedAt));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public async Task<OpResult> DecideAsync(int id, bool approve, AdminScope s)
    {
        if (s.Role is not (AdminRole.Admin or AdminRole.WarehouseManager)) return OpResult.Fail("ليس لديك صلاحية لتنفيذ هذه العملية");
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
