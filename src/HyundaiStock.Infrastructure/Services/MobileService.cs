using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record MobileWarehouse(int Id, string Name, string Type, int BranchId, string BranchName);
public sealed record MobileProfile(int Id, string Name, string PhoneNumber, List<MobileWarehouse> Warehouses, bool CanPickDuplicateRow = false);
public sealed record MobileSession(int Id, int Year, string Name, string Status, DateTime StartedAt);
/// <summary>RowNo / RowCount: position of this row among the rows that share its part number in the same warehouse (1 when the number does not repeat).</summary>
public sealed record MobilePart(int Id, string PartNumber, string Description, string? SystemLocation, int? WarehouseId, string? WarehouseName, int? CountedQuantity, string? CountedLocation, DateTime? CountedAt, bool CountedByMe, bool PendingApproval = false, int RowNo = 1, int RowCount = 1);
public sealed record MobileVin(int Id, string Vin, int WarehouseId, string WarehouseName, double? Latitude, double? Longitude, DateTime CreatedAt, bool IsExpected, string? VehicleType = null, string? Dealer = null, int? ManufactureYear = null);
public sealed record MobileVinLookup(string Vin, bool IsExpected, string? VehicleType, bool AlreadyScanned, string? Dealer = null, int? ManufactureYear = null);
public sealed record MobileRecount(int Id, int PartId, string PartNumber, string Description, string? SystemLocation, string WarehouseName, string RequestedBy, DateTime CreatedAt, string Status = "قيد الانتظار", int? ResultQuantity = null, DateTime? CompletedAt = null, int? WarehouseId = null, int? BranchId = null, string? BranchName = null);
public sealed record MobileVinRequest(int Id, string CurrentVin, string NewVin, string Reason, string Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record MobileQtyRequest(int Id, int? PartId, string PartNumber, string Description, int? OldQuantity, int NewQuantity, string Reason, string Status, DateTime CreatedAt, bool CanEdit = false);
public sealed record HistoryItem(string Type, int Id, string Reference, string? Detail, int WarehouseId, string WarehouseName, DateTime At);

public sealed class CountInput { public int? PartId { get; set; } public string? PartNumber { get; set; } public string? Description { get; set; } public int? WarehouseId { get; set; } public int? Quantity { get; set; } public string? Location { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } public bool Confirm { get; set; } }
public sealed record CountedSnapshot(int? CountedQuantity, string? CountedLocation, DateTime? CountedAt);
public sealed class ScanInput { public string? Vin { get; set; } public int? WarehouseId { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }
public sealed class VinCorrectionInput { public int? ScannedVinId { get; set; } public string? NewVin { get; set; } public string? Reason { get; set; } }
public sealed class QtyRequestInput { public int? PartId { get; set; } public int? NewQuantity { get; set; } public string? Reason { get; set; } }
public sealed class ChangePasswordInput { public string? CurrentPassword { get; set; } public string? NewPassword { get; set; } }
public sealed class RecountInput { public int? Quantity { get; set; } public string? Location { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } public bool Confirm { get; set; } }

public sealed record MobileResult<T>(T? Data, string? Error, int Status = 200, object? Detail = null, string? Message = null);

public sealed class MobileService(AppDbContext db, SessionLookup sessions, IPasswordService pw)
{
    public async Task<AppUser?> LoginAsync(string? phone, string? password)
    {
        phone = AccountPhones.Normalize(phone);
        if (!Validators.IsIraqiPhone(phone) || string.IsNullOrEmpty(password)) return null;
        // Mobile credentials belong to AppUsers only. A legacy Admin duplicate
        // must not disable a valid employee or grant dashboard privileges.
        var users = await db.AppUsers.Where(x => x.IsActive).ToListAsync();
        var matches = users.Where(x => AccountPhones.Normalize(x.PhoneNumber) == phone).ToList();
        if (matches.Count != 1) return null;
        return pw.Verify(matches[0].PasswordHash, password) ? matches[0] : null;
    }

    public Task<bool> IsActiveAsync(int userId) =>
        db.AppUsers.AsNoTracking().AnyAsync(x => x.Id == userId && x.IsActive);

    private Task<List<int>> MyWarehouses(int userId) => db.AppUserWarehouses.Where(x => x.AppUserId == userId).Select(x => x.WarehouseId).ToListAsync();

    /// <summary>One assigned warehouse can be omitted. Several require an explicit assigned id. The branch always comes from that warehouse.</summary>
    private async Task<(int? Id, string? Error, int Status)> ResolveWarehouseAsync(int userId, int? requested)
    {
        var whs = (await MyWarehouses(userId)).Distinct().ToList();
        if (whs.Count == 0) return (null, "لا يوجد مستودع مرتبط بحسابك", 400);
        if (requested is int id)
            return whs.Contains(id) ? (id, null, 200) : (null, "المستودع غير مرتبط بحسابك", 403);
        if (whs.Count == 1) return (whs[0], null, 200);
        return (null, "اختر المستودع", 400);
    }

    public async Task<MobileResult<object>> RegisterDeviceAsync(int userId, string? token, string? platform)
    {
        var t = token?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length is < 20 or > 512) return new(null, "رمز الجهاز غير صالح", 400);
        var plat = string.Equals(platform, "ios", StringComparison.OrdinalIgnoreCase) ? "ios" : "android";
        var row = await db.DeviceRegistrations.FirstOrDefaultAsync(x => x.Token == t);
        if (row == null) db.DeviceRegistrations.Add(new DeviceRegistration { AppUserId = userId, Token = t, Platform = plat });
        else { row.AppUserId = userId; row.Platform = plat; row.IsActive = true; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        return new(null, null, 200);
    }

    public async Task<MobileResult<object>> DeactivateDeviceAsync(int userId, string? token)
    {
        var t = token?.Trim();
        var q = db.DeviceRegistrations.Where(x => x.AppUserId == userId && x.IsActive);
        if (!string.IsNullOrEmpty(t)) q = q.Where(x => x.Token == t);
        var rows = await q.ToListAsync();
        foreach (var row in rows) { row.IsActive = false; row.UpdatedAt = DateTime.UtcNow; }
        await db.SaveChangesAsync();
        return new(null, null, 200);
    }
    /// <summary>Branches of the user's warehouses: parts imported per branch (BranchNo. only) are visible to every warehouse in that branch.</summary>
    private Task<List<int>> MyBranches(int userId) => db.AppUserWarehouses.Where(x => x.AppUserId == userId).Select(x => x.Warehouse!.BranchId).Distinct().ToListAsync();
    private static bool CanSee(InventoryPart p, List<int> whs, List<int> brs) =>
        p.WarehouseId is int w ? whs.Contains(w) : p.BranchId is int b && brs.Contains(b);

    public async Task<MobileProfile?> ProfileAsync(int userId)
    {
        var u = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) return null;
        var whs = await db.AppUserWarehouses.Where(x => x.AppUserId == userId)
            .Select(x => new MobileWarehouse(x.WarehouseId, x.Warehouse!.Name, x.Warehouse.Type == WarehouseType.Type1 ? "نوع 1" : "نوع 2", x.Warehouse.BranchId, x.Warehouse.Branch!.Name)).ToListAsync();
        return new MobileProfile(u.Id, u.Name, u.PhoneNumber, whs, u.CanPickDuplicateRow);
    }

    public async Task<MobileSession?> CurrentSessionAsync()
    {
        var s = await sessions.CurrentAsync();
        return s == null ? null : new MobileSession(s.Id, s.Year, $"جلسة جرد {s.Year}", Labels.Session(s.Status), s.StartedAt);
    }

    public async Task<MobileResult<PagedResult<MobilePart>>> PartsAsync(int userId, PageQuery p, int? warehouseId, bool? counted, bool mine = false, string? code = null)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        var whs = await MyWarehouses(userId);
        if (warehouseId is int w && !whs.Contains(w)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        var brs = await MyBranches(userId);
        var q = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == s.Id &&
            ((x.WarehouseId != null && whs.Contains(x.WarehouseId.Value)) || (x.WarehouseId == null && x.BranchId != null && brs.Contains(x.BranchId.Value))));
        if (warehouseId is int ww)
        {
            var wb = await db.Warehouses.Where(z => z.Id == ww).Select(z => (int?)z.BranchId).FirstOrDefaultAsync();
            q = q.Where(x => x.WarehouseId == ww || (x.WarehouseId == null && x.BranchId == wb));
        }
        if (counted == true) q = q.Where(x => x.CountedQuantity != null);
        if (counted == false) q = q.Where(x => x.CountedQuantity == null);
        if (mine) q = q.Where(x => x.CountedByUserId == userId && x.CountedQuantity != null);
        if (p.Term is string t)
        {
            var tn = NormalizePartCode(t);
            q = q.Where(x => x.PartNumber.Contains(t) || x.Description.Contains(t) || (tn.Length >= 3 && x.PartNumber.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper().Contains(tn)));
        }
        if (code != null)
        {
            var cn = NormalizePartCode(code);
            q = q.Where(x => x.PartNumber.Replace(" ", "").Replace("-", "").Replace(".", "").ToUpper() == cn);
            var all = await q.OrderBy(x => x.WarehouseId == null ? 1 : 0).ThenBy(x => x.Id).Select(x => new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId, x.Warehouse != null ? x.Warehouse.Name : null,
                x.CountedQuantity, x.CountedLocation, x.CountedAt, x.CountedByUserId == userId,
                db.QuantityCorrections.Any(r => r.InventoryPartId == x.Id && r.Status == RequestStatus.Pending), 1, 1)).ToListAsync();
            // Number the rows that share a part number inside one warehouse, in import order.
            var numbered = all.GroupBy(x => x.WarehouseId).SelectMany(g => g.Select((x, n) => x with { RowNo = n + 1, RowCount = g.Count() })).ToList();
            if (!await CanPickRowAsync(userId))
                numbered = numbered.GroupBy(x => x.WarehouseId).Select(g => DefaultRow(g)).ToList();
            var page = numbered.Skip((p.SafePage - 1) * p.SafeSize).Take(p.SafeSize).ToList();
            return new(new PagedResult<MobilePart> { Items = page, Page = p.SafePage, PageSize = p.SafeSize, TotalCount = numbered.Count }, null);
        }
        var rows = q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id).Select(x => new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId, x.Warehouse != null ? x.Warehouse.Name : null,
            x.CountedQuantity, x.CountedLocation, x.CountedAt, x.CountedByUserId == userId,
            db.QuantityCorrections.Any(r => r.InventoryPartId == x.Id && r.Status == RequestStatus.Pending), 1, 1));
        return new(Paging.Build(rows, p), null);
    }

    public const string RowPickDenied = "ليس لديك صلاحية اختيار سطر القطعة المكررة";

    private Task<bool> CanPickRowAsync(int userId) => db.AppUsers.AsNoTracking().Where(x => x.Id == userId).Select(x => x.CanPickDuplicateRow).FirstOrDefaultAsync();

    /// <summary>The row a counter without the pick grant counts: the first uncounted row, else the first row. Same order as counting by part number.</summary>
    private static MobilePart DefaultRow(IEnumerable<MobilePart> rows) =>
        rows.OrderBy(x => x.CountedQuantity == null ? 0 : 1).ThenBy(x => x.RowNo).First();

    /// <summary>Uppercase and keep only letters/digits so "58310 BWA40", "58310-BWA40" and "58310BWA40" match.</summary>
    public static string NormalizePartCode(string? v) => new string((v ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    public async Task<MobileResult<MobilePart>> PartAsync(int userId, int id)
    {
        var whs = await MyWarehouses(userId); var brs = await MyBranches(userId);
        var x = await db.InventoryParts.AsNoTracking().Include(z => z.Warehouse).FirstOrDefaultAsync(z => z.Id == id);
        if (x == null || !CanSee(x, whs, brs)) return new(null, "القطعة غير موجودة", 404);
        var pending = await db.QuantityCorrections.AnyAsync(r => r.InventoryPartId == x.Id && r.Status == RequestStatus.Pending);
        return new(new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId, x.Warehouse?.Name, x.CountedQuantity, x.CountedLocation, x.CountedAt, x.CountedByUserId == userId, pending), null);
    }

    public Task<MobileResult<MobilePart>> CountAsync(int userId, CountInput i) => CountAsync(userId, i, assignedRow: false);

    private async Task<MobileResult<MobilePart>> CountAsync(int userId, CountInput i, bool assignedRow)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        if (i.Quantity is null || i.Quantity < 0) return new(null, "الكمية مطلوبة ويجب ألا تكون سالبة", 400);
        if (Validators.GpsError(i.Latitude, i.Longitude) is string g) return new(null, g, 400);
        var user = await db.AppUsers.FirstAsync(x => x.Id == userId);
        var whs = await MyWarehouses(userId); var brs = await MyBranches(userId);
        if (whs.Count == 0) return new(null, "لا يوجد مستودع مرتبط بحسابك", 400);
        InventoryPart? part;
        if (i.PartId is int pid)
        {
            part = await db.InventoryParts.FirstOrDefaultAsync(x => x.Id == pid && x.SessionId == s.Id);
            if (part == null) return new(null, "القطعة غير موجودة في الجلسة الحالية", 404);
            if (!assignedRow && !user.CanPickDuplicateRow)
            {
                var pid0 = part.Id; var pn0 = part.PartNumber; var wh0 = part.WarehouseId; var br0 = part.BranchId;
                var siblings = await db.InventoryParts.AsNoTracking().Where(x => x.SessionId == s.Id && x.PartNumber == pn0 && x.WarehouseId == wh0 && (wh0 != null || x.BranchId == br0))
                    .OrderBy(x => x.CountedQuantity == null ? 0 : 1).ThenBy(x => x.Id).Select(x => x.Id).ToListAsync();
                if (siblings.Count > 1 && siblings[0] != pid0) return new(null, RowPickDenied, 403);
            }
            // The row already names its warehouse. Do not ask again when the client omitted one.
            if (i.WarehouseId is null && part.WarehouseId is int own && whs.Contains(own)) i.WarehouseId = own;
        }
        else
        {
            var resolved = await ResolveWarehouseAsync(userId, i.WarehouseId);
            if (resolved.Error != null) return new(null, resolved.Error, resolved.Status);
            i.WarehouseId = resolved.Id;
            var pn = i.PartNumber?.Trim();
            if (string.IsNullOrEmpty(pn) || pn.Length > 64) return new(null, "رقم القطعة أو partId مطلوب", 400);
            if (i.WarehouseId is not int wid) return new(null, "المستودع مطلوب", 400);
            var wbr = await db.Warehouses.Where(z => z.Id == wid).Select(z => (int?)z.BranchId).FirstOrDefaultAsync();
            part = await db.InventoryParts.Where(x => x.SessionId == s.Id && x.PartNumber == pn && (x.WarehouseId == wid || (x.WarehouseId == null && x.BranchId == wbr)))
                .OrderBy(x => x.WarehouseId == null ? 1 : 0).ThenBy(x => x.CountedQuantity == null ? 0 : 1).ThenBy(x => x.Id).FirstOrDefaultAsync();
            if (part == null) return new(null, "رمز القطعة غير موجود بالنظام", 404);
        }
        if (!CanSee(part, whs, brs)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        if (i.WarehouseId is int requestedWarehouse)
        {
            if (!whs.Contains(requestedWarehouse)) return new(null, "المستودع غير مرتبط بحسابك", 403);
            var requestedBranch = await db.Warehouses.Where(w => w.Id == requestedWarehouse).Select(w => w.BranchId).FirstAsync();
            if ((part.WarehouseId is int actual && actual != requestedWarehouse) ||
                (part.BranchId is int branch && branch != requestedBranch))
                return new(null, "المخزن لا يطابق فرع ومخزن القطعة", 400);
        }
        // Branch-level part (imported with BranchNo. only): pin it to the warehouse it was counted in.
        if (part.WarehouseId == null && i.WarehouseId is int cw && whs.Contains(cw)) part.WarehouseId = cw;
        if (part.WarehouseId == null)
            part.WarehouseId = await db.Warehouses.Where(z => whs.Contains(z.Id) && z.BranchId == part.BranchId).OrderBy(z => z.Id).Select(z => (int?)z.Id).FirstOrDefaultAsync();
        if (part.CountedQuantity != null && !i.Confirm)
            return new(null, "هذه القطعة معدودة مسبقًا", 409, new CountedSnapshot(part.CountedQuantity, part.CountedLocation, part.CountedAt));
        var loc = i.Location?.Trim(); if (loc?.Length > 64) loc = loc[..64];
        // A count above the system quantity is not written to the inventory. It becomes a quantity request and is added only after approval.
        if (i.Quantity > part.SystemQuantity) return await HoldSurplusAsync(user, part, i.Quantity.Value, loc);
        if (part.Id != 0)
        {
            if (part.CountedQuantity != i.Quantity)
                db.PartChangeLogs.Add(new PartChangeLog { InventoryPartId = part.Id, Field = "الكمية المحسوبة", OldValue = part.CountedQuantity?.ToString(), NewValue = i.Quantity.ToString(), ChangedBy = user.Name, Source = "التطبيق" });
            if ((part.CountedLocation ?? "") != (loc ?? ""))
                db.PartChangeLogs.Add(new PartChangeLog { InventoryPartId = part.Id, Field = "الموقع المحسوب", OldValue = part.CountedLocation, NewValue = loc, ChangedBy = user.Name, Source = "التطبيق" });
        }
        part.CountedQuantity = i.Quantity; part.CountedLocation = loc; part.CountedByUserId = userId; part.CountedAt = DateTime.UtcNow;
        part.Latitude = i.Latitude; part.Longitude = i.Longitude;
        await db.SaveChangesAsync();
        return await PartAsync(userId, part.Id);
    }

    public const string SurplusHeldMessage = "تم استلام العد، وسيُضاف للجرد بعد الموافقة";

    private async Task<MobileResult<MobilePart>> HoldSurplusAsync(AppUser user, InventoryPart part, int quantity, string? loc)
    {
        if (await db.QuantityCorrections.AnyAsync(x => x.InventoryPartId == part.Id && x.Status == RequestStatus.Pending))
            return new(null, "يوجد طلب تعديل قيد الانتظار لنفس القطعة", 409);
        var reason = "عدد أعلى من كمية النظام بانتظار الموافقة" + (string.IsNullOrEmpty(loc) ? "" : $" · الموقع: {loc}");
        // Keep the warehouse pin from this count; only the quantity waits for approval.
        await db.SaveChangesAsync();
        var r = new QuantityCorrectionRequest
        {
            InventoryPartId = part.Id, PartNumber = part.PartNumber, PartDescription = part.Description, OldQuantity = part.CountedQuantity, NewQuantity = quantity,
            Reason = reason, BranchId = part.BranchId, WarehouseId = part.WarehouseId, RequestedByUserId = user.Id
        };
        db.QuantityCorrections.Add(r);
        await db.SaveChangesAsync();
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = r.Id, Action = AuditActions.Submitted, ToStatus = "PendingWarehouseApproval", ActorUserId = user.Id, ActorRole = "Counter", Reason = reason, BranchId = part.BranchId, WarehouseId = part.WarehouseId, InventoryPartId = part.Id, SessionId = part.SessionId });
        await db.SaveChangesAsync();
        var shown = await PartAsync(user.Id, part.Id);
        return new(shown.Data, null, 202, null, SurplusHeldMessage);
    }

    public async Task<MobileResult<MobileVin>> ScanVinAsync(int userId, ScanInput i)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        var vin = Validators.NormalizeVin(i.Vin);
        if (!Validators.IsVin(vin)) return new(null, Validators.VinError, 400);
        if (Validators.GpsError(i.Latitude, i.Longitude) is string g) return new(null, g, 400);
        var resolved = await ResolveWarehouseAsync(userId, i.WarehouseId);
        if (resolved.Error != null) return new(null, resolved.Error, resolved.Status);
        var wid = resolved.Id!.Value;
        var whs = await MyWarehouses(userId);
        if (!whs.Contains(wid)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        if (await db.ScannedVins.AnyAsync(x => x.SessionId == s.Id && x.Vin == vin)) return new(null, "رقم VIN ممسوح مسبقًا في هذه الجلسة", 409);
        var wh = await db.Warehouses.FirstAsync(x => x.Id == wid);
        var e = new ScannedVin { SessionId = s.Id, Vin = vin, UserId = userId, WarehouseId = wid, BranchId = wh.BranchId, Latitude = i.Latitude, Longitude = i.Longitude };
        db.ScannedVins.Add(e);
        await db.SaveChangesAsync();
        var exp = await db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == s.Id && x.Vin == vin).Select(x => new { x.Model, x.Dealer, x.ManufactureYear }).FirstOrDefaultAsync();
        return new(new MobileVin(e.Id, e.Vin, wid, wh.Name, e.Latitude, e.Longitude, e.CreatedAt, exp != null, exp?.Model, exp?.Dealer, exp?.ManufactureYear), null, 201);
    }

    /// <summary>Vehicle type for a VIN read by the app (from the session file), before it is saved.</summary>
    public async Task<MobileResult<MobileVinLookup>> LookupVinAsync(string? raw)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        var vin = Validators.NormalizeVin(raw);
        if (!Validators.IsVin(vin)) return new(null, Validators.VinError, 400);
        var exp = await db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == s.Id && x.Vin == vin).Select(x => new { x.Model, x.Dealer, x.ManufactureYear }).FirstOrDefaultAsync();
        var scanned = await db.ScannedVins.AnyAsync(x => x.SessionId == s.Id && x.Vin == vin);
        return new(new MobileVinLookup(vin, exp != null, exp?.Model, scanned, exp?.Dealer, exp?.ManufactureYear), null, 200);
    }

    public async Task<PagedResult<MobileVin>> MyVinsAsync(int userId, PageQuery p)
    {
        var s = await sessions.CurrentAsync();
        var sid = s?.Id ?? -1;
        var q = db.ScannedVins.AsNoTracking().Where(x => x.UserId == userId && x.SessionId == sid);
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).Select(x => new MobileVin(x.Id, x.Vin, x.WarehouseId, x.Warehouse!.Name, x.Latitude, x.Longitude, x.CreatedAt,
            db.ExpectedVins.Any(e => e.SessionId == x.SessionId && e.Vin == x.Vin),
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.Model).FirstOrDefault(),
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.Dealer).FirstOrDefault(),
            db.ExpectedVins.Where(e => e.SessionId == x.SessionId && e.Vin == x.Vin).Select(e => e.ManufactureYear).FirstOrDefault()));
        return Paging.Build(rows, p);
    }

    public async Task<MobileResult<MobileVinRequest>> VinCorrectionAsync(int userId, VinCorrectionInput i)
    {
        if (i.ScannedVinId is not int sid) return new(null, "scannedVinId مطلوب", 400);
        var newVin = Validators.NormalizeVin(i.NewVin);
        if (!Validators.IsVin(newVin)) return new(null, Validators.VinError, 400);
        var reason = i.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return new(null, "السبب مطلوب", 400);
        if (reason.Length > 1000) reason = reason[..1000];
        var v = await db.ScannedVins.Include(x => x.Session).FirstOrDefaultAsync(x => x.Id == sid && x.UserId == userId);
        if (v == null) return new(null, "المركبة غير موجودة", 404);
        if (v.Session!.Status != SessionStatus.InProgress) return new(null, "جلسة الجرد منتهية", 409);
        if (v.Vin == newVin) return new(null, "رقم VIN الجديد مطابق للحالي", 400);
        if (await db.VinCorrections.AnyAsync(x => x.ScannedVinId == sid && x.Status == RequestStatus.Pending)) return new(null, "يوجد طلب تصحيح قيد الانتظار لنفس المركبة", 409);
        var r = new VinCorrectionRequest { ScannedVinId = sid, CurrentVin = v.Vin, NewVin = newVin, Reason = reason, RequestedByUserId = userId };
        db.VinCorrections.Add(r);
        await db.SaveChangesAsync();
        return new(new MobileVinRequest(r.Id, r.CurrentVin, r.NewVin, r.Reason, Labels.Vin(r.Status), r.CreatedAt, r.ApprovedAt), null, 201);
    }

    public Task<PagedResult<MobileVinRequest>> MyVinCorrectionsAsync(int userId, PageQuery p)
    {
        var q = db.VinCorrections.AsNoTracking().Where(x => x.RequestedByUserId == userId).OrderByDescending(x => x.CreatedAt)
            .Select(x => new MobileVinRequest(x.Id, x.CurrentVin, x.NewVin, x.Reason, x.Status == RequestStatus.Pending ? "قيد الانتظار" : x.Status == RequestStatus.Approved ? "موافق عليه" : "مرفوض", x.CreatedAt, x.ApprovedAt));
        return Task.FromResult(Paging.Build(q, p));
    }

    public Task<PagedResult<MobileRecount>> RecountsAsync(int userId, PageQuery p, bool includeCompleted = false)
    {
        var q = db.RecountRequests.AsNoTracking().Where(x => x.AssignedUserId == userId && (includeCompleted || x.Status == RecountStatus.Pending) && x.InventoryPart!.Session!.Status == SessionStatus.InProgress)
            .OrderBy(x => x.CreatedAt).Select(x => new MobileRecount(x.Id, x.InventoryPartId, x.InventoryPart!.PartNumber, x.InventoryPart.Description, x.InventoryPart.SystemLocation,
                x.InventoryPart.Warehouse != null ? x.InventoryPart.Warehouse.Name : "", x.RequestedByAdmin!.Name, x.CreatedAt, x.Status == RecountStatus.Completed ? "مكتمل" : "قيد المعالجة", x.ResultQuantity, x.CompletedAt,
                x.InventoryPart.WarehouseId, x.InventoryPart.BranchId, x.InventoryPart.Branch != null ? x.InventoryPart.Branch.Name : null));
        return Task.FromResult(Paging.Build(q, p));
    }

    public async Task<MobileResult<MobilePart>> SubmitRecountAsync(int userId, int id, RecountInput i)
    {
        var r = await db.RecountRequests.Include(x => x.InventoryPart).ThenInclude(p => p!.Session).FirstOrDefaultAsync(x => x.Id == id && x.AssignedUserId == userId);
        if (r == null) return new(null, "طلب إعادة العد غير موجود", 404);
        if (r.Status != RecountStatus.Pending) return new(null, "طلب إعادة العد مكتمل", 409);
        if (r.InventoryPart!.Session!.Status != SessionStatus.InProgress) return new(null, "جلسة الجرد منتهية", 409);
        var res = await CountAsync(userId, new CountInput { PartId = r.InventoryPartId, Quantity = i.Quantity, Location = i.Location, Latitude = i.Latitude, Longitude = i.Longitude, Confirm = i.Confirm }, assignedRow: true);
        if (res.Error != null) return res;
        r.Status = RecountStatus.Completed; r.ResultQuantity = i.Quantity; r.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return res;
    }

    public async Task<MobileResult<MobileQtyRequest>> QtyRequestAsync(int userId, QtyRequestInput i)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        if (i.PartId is not int pid) return new(null, "القطعة مطلوبة", 400);
        if (i.NewQuantity is null || i.NewQuantity < 0) return new(null, "الكمية الجديدة مطلوبة ويجب ألا تكون سالبة", 400);
        var reason = i.Reason?.Trim() ?? "";
        if (reason.Length == 0) return new(null, "السبب مطلوب", 400);
        if (reason.Length > 1000) reason = reason[..1000];
        var whs = await MyWarehouses(userId);
        var part = await db.InventoryParts.FirstOrDefaultAsync(x => x.Id == pid && x.SessionId == s.Id);
        if (part == null) return new(null, "القطعة غير موجودة في الجلسة الحالية", 404);
        if (part.WarehouseId is not int w || !whs.Contains(w)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        if (await db.QuantityCorrections.AnyAsync(x => x.InventoryPartId == pid && x.Status == RequestStatus.Pending)) return new(null, "يوجد طلب تعديل قيد الانتظار لنفس القطعة", 409);
        var r = new QuantityCorrectionRequest { InventoryPartId = pid, PartNumber = part.PartNumber, PartDescription = part.Description, OldQuantity = part.CountedQuantity, NewQuantity = i.NewQuantity.Value, Reason = reason, BranchId = part.BranchId, WarehouseId = part.WarehouseId, RequestedByUserId = userId };
        db.QuantityCorrections.Add(r);
        await db.SaveChangesAsync();
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = r.Id, Action = AuditActions.Submitted, ToStatus = "PendingWarehouseApproval", ActorUserId = userId, ActorRole = "Counter", Reason = reason, BranchId = part.BranchId, WarehouseId = part.WarehouseId, InventoryPartId = pid, SessionId = part.SessionId });
        await db.SaveChangesAsync();
        return new(new MobileQtyRequest(r.Id, pid, r.PartNumber, r.PartDescription, r.OldQuantity, r.NewQuantity, r.Reason, Labels.QtyMobile(r.Status, r.ApprovalLevel), r.CreatedAt, true), null, 201);
    }

    public async Task<MobileResult<MobileQtyRequest>> EditQtyRequestAsync(int userId, int id, QtyRequestInput i)
    {
        var r = await db.QuantityCorrections.AsNoTracking().Include(x => x.InventoryPart).ThenInclude(x => x!.Session)
            .FirstOrDefaultAsync(x => x.Id == id && x.RequestedByUserId == userId);
        if (r == null) return new(null, "الطلب غير موجود", 404);
        if (r.Status != RequestStatus.Pending || r.ApprovalLevel != ApprovalLevel.None)
            return new(null, "لا يمكن تعديل الطلب بعد الموافقة عليه", 409);
        if (r.InventoryPart?.Session?.Status != SessionStatus.InProgress) return new(null, "جلسة الجرد منتهية", 409);
        if (i.NewQuantity is null or < 0 || string.IsNullOrWhiteSpace(i.Reason)) return new(null, "الكمية والسبب مطلوبان", 400);
        if (i.PartId != null && i.PartId != r.InventoryPartId) return new(null, "لا يمكن تغيير قطعة الطلب", 400);
        var whs = await MyWarehouses(userId);
        if (r.WarehouseId is not int w || !whs.Contains(w)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        var reason = i.Reason.Trim(); if (reason.Length > 1000) reason = reason[..1000];
        await using var tx = await db.Database.BeginTransactionAsync();
        var changed = await db.QuantityCorrections.Where(x => x.Id == id && x.RequestedByUserId == userId && x.Status == RequestStatus.Pending && x.ApprovalLevel == ApprovalLevel.None)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.NewQuantity, i.NewQuantity.Value).SetProperty(x => x.Reason, reason).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        if (changed != 1) return new(null, "تم التعامل مع هذا الطلب مسبقاً", 409);
        db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.Quantity, SubjectId = id, Action = "Edited", ToStatus = "PendingWarehouseApproval", ActorUserId = userId, ActorRole = "Counter", Reason = reason, BranchId = r.BranchId, WarehouseId = r.WarehouseId, InventoryPartId = r.InventoryPartId, SessionId = r.InventoryPart!.SessionId });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return new(new MobileQtyRequest(r.Id, r.InventoryPartId, r.PartNumber, r.PartDescription, r.OldQuantity, i.NewQuantity.Value, reason, Labels.QtyMobile(r.Status, r.ApprovalLevel), r.CreatedAt, true), null);
    }

    public async Task<PagedResult<MobileQtyRequest>> MyQtyRequestsAsync(int userId, PageQuery p)
    {
        var q = db.QuantityCorrections.AsNoTracking().Where(x => x.RequestedByUserId == userId).OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new MobileQtyRequest(x.Id, x.InventoryPartId, x.PartNumber, x.PartDescription, x.OldQuantity, x.NewQuantity, x.Reason,
                x.Status == RequestStatus.Approved ? "مقبول" : x.Status == RequestStatus.Rejected ? "مرفوض" : x.ApprovalLevel == ApprovalLevel.First ? "بانتظار المالية" : "بانتظار مدير المخزن", x.CreatedAt, x.Status == RequestStatus.Pending && x.ApprovalLevel == ApprovalLevel.None && x.InventoryPart!.Session!.Status == SessionStatus.InProgress));
        return Paging.Build(q, p);
    }

    public async Task<MobileResult<bool>> ChangePasswordAsync(int userId, ChangePasswordInput i)
    {
        var u = await db.AppUsers.FirstAsync(x => x.Id == userId);
        if (string.IsNullOrEmpty(i.CurrentPassword) || !pw.Verify(u.PasswordHash, i.CurrentPassword)) return new(false, "كلمة المرور الحالية غير صحيحة", 400);
        if (string.IsNullOrEmpty(i.NewPassword) || i.NewPassword.Length < 6) return new(false, "كلمة المرور 6 أحرف على الأقل", 400);
        if (i.NewPassword == i.CurrentPassword) return new(false, "يجب أن تختلف كلمة المرور الجديدة عن الحالية", 400);
        u.PasswordHash = pw.Hash(i.NewPassword);
        await db.SaveChangesAsync();
        return new(true, null);
    }

    public async Task<PagedResult<HistoryItem>> HistoryAsync(int userId, PageQuery p, string? type)
    {
        var s = await sessions.CurrentAsync();
        var sid = s?.Id ?? -1;
        var partsQ = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid && x.CountedByUserId == userId && x.CountedAt != null);
        var vinsQ = db.ScannedVins.AsNoTracking().Where(x => x.SessionId == sid && x.UserId == userId);
        if (p.Term is string t) { partsQ = partsQ.Where(x => x.PartNumber.Contains(t)); vinsQ = vinsQ.Where(x => x.Vin.Contains(t)); }
        var parts = partsQ.Select(x => new HistoryItem("part", x.Id, x.PartNumber, x.CountedQuantity.ToString(), x.WarehouseId!.Value, x.Warehouse!.Name, x.CountedAt!.Value));
        var vins = vinsQ.Select(x => new HistoryItem("vin", x.Id, x.Vin, null, x.WarehouseId, x.Warehouse!.Name, x.CreatedAt));
        var all = new List<HistoryItem>();
        if (type != "vin") all.AddRange(await parts.ToListAsync());
        if (type != "part") all.AddRange(await vins.ToListAsync());
        all = all.OrderByDescending(x => x.At).ToList();
        var page = p.SafePage; var size = p.SafeSize;
        return new PagedResult<HistoryItem> { Items = all.Skip((page - 1) * size).Take(size).ToList(), Page = page, PageSize = size, TotalCount = all.Count };
    }
}
