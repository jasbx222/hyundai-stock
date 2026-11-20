using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record MobileWarehouse(int Id, string Name, string Type, int BranchId, string BranchName);
/// <summary>CountType: parts | vehicles | both. CanCountParts / CanScanVins say which home icons the app shows; the API enforces the same rule.</summary>
public sealed record MobileProfile(int Id, string Name, string PhoneNumber, List<MobileWarehouse> Warehouses, bool CanPickDuplicateRow = false, string CountType = "both", bool CanCountParts = true, bool CanScanVins = true);
public sealed record MobileSession(int Id, int Year, string Name, string Status, DateTime StartedAt);
/// <summary>
/// RowNo / RowCount: position of this row among the rows that share its part number in the same warehouse (1 when the number does not repeat).
/// IsCounted / CountedBy: whether the row is counted and by whom. The counted quantity, location and time are sent only to the counter who counted it.
/// PendingApproval is always false: counts are no longer held for approval. Kept for older app builds.
/// </summary>
public sealed record MobilePart(int Id, string PartNumber, string Description, string? SystemLocation, int? WarehouseId, string? WarehouseName, int? CountedQuantity, string? CountedLocation, DateTime? CountedAt, bool CountedByMe, bool PendingApproval = false, int RowNo = 1, int RowCount = 1, bool IsCounted = false, string? CountedBy = null);
public sealed record MobileVin(int Id, string Vin, int WarehouseId, string WarehouseName, double? Latitude, double? Longitude, DateTime CreatedAt, bool IsExpected, string? VehicleType = null, string? Dealer = null, int? ManufactureYear = null);
public sealed record MobileVinLookup(string Vin, bool IsExpected, string? VehicleType, bool AlreadyScanned, string? Dealer = null, int? ManufactureYear = null, string? ScannedBy = null, bool ScannedByMe = false);
public sealed record MobileRecount(int Id, int PartId, string PartNumber, string Description, string? SystemLocation, string WarehouseName, string RequestedBy, DateTime CreatedAt, string Status = "قيد الانتظار", int? ResultQuantity = null, DateTime? CompletedAt = null, int? WarehouseId = null, int? BranchId = null, string? BranchName = null);
public sealed record MobileVinRequest(int Id, string CurrentVin, string NewVin, string Reason, string Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record HistoryItem(string Type, int Id, string Reference, string? Detail, int WarehouseId, string WarehouseName, DateTime At);

public sealed class CountInput { public int? PartId { get; set; } public string? PartNumber { get; set; } public string? Description { get; set; } public int? WarehouseId { get; set; } public int? Quantity { get; set; } public string? Location { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } public bool Confirm { get; set; } }
public sealed record MobileRecountAsk(int Id, int PartId, string PartNumber, string Status, DateTime CreatedAt);
/// <summary>Error detail when another user already counted the row. Carries the name only, never the counted quantity.</summary>
public sealed record CountedByDetail(string? CountedBy);
/// <summary>Error detail when a VIN was already scanned in the session. Carries the employee name only.</summary>
public sealed record ScannedByDetail(string? ScannedBy, bool ScannedByMe);
public sealed class ScanInput { public string? Vin { get; set; } public int? WarehouseId { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }
public sealed class VinCorrectionInput { public int? ScannedVinId { get; set; } public string? NewVin { get; set; } public string? Reason { get; set; } }
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
        return new MobileProfile(u.Id, u.Name, u.PhoneNumber, whs, u.CanPickDuplicateRow, Labels.CountTypeCode(u.CountType), CountTypes.Parts(u.CountType), CountTypes.Vehicles(u.CountType));
    }

    public async Task<MobileSession?> CurrentSessionAsync()
    {
        var s = await sessions.CurrentAsync();
        return s == null ? null : new MobileSession(s.Id, s.Year, s.Name.Length > 0 ? s.Name : Labels.SessionName(s.Kind, s.Year, s.Month), Labels.Session(s.Status), s.StartedAt);
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
            var all = await Project(q.OrderBy(x => x.WarehouseId == null ? 1 : 0).ThenBy(x => x.Id), userId).ToListAsync();
            // Number the rows that share a part number inside one warehouse, in import order.
            var numbered = all.GroupBy(x => x.WarehouseId).SelectMany(g => g.Select((x, n) => x with { RowNo = n + 1, RowCount = g.Count() })).ToList();
            if (!await CanPickRowAsync(userId))
                numbered = numbered.GroupBy(x => x.WarehouseId).Select(g => DefaultRow(g)).ToList();
            var page = numbered.Skip((p.SafePage - 1) * p.SafeSize).Take(p.SafeSize).ToList();
            return new(new PagedResult<MobilePart> { Items = page, Page = p.SafePage, PageSize = p.SafeSize, TotalCount = numbered.Count }, null);
        }
        var rows = Project(q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id), userId);
        return new(Paging.Build(rows, p), null);
    }

    /// <summary>Another counter's quantity, location and time stay hidden. Only the name of who counted it is shown.</summary>
    private static IQueryable<MobilePart> Project(IQueryable<InventoryPart> q, int userId) => q.Select(x => new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId, x.Warehouse != null ? x.Warehouse.Name : null,
        x.CountedByUserId == userId ? x.CountedQuantity : null, x.CountedByUserId == userId ? x.CountedLocation : null, x.CountedByUserId == userId ? x.CountedAt : null,
        x.CountedByUserId == userId, false, 1, 1, x.CountedQuantity != null, x.CountedQuantity != null && x.CountedByUser != null ? x.CountedByUser.Name : null));

    public const string RowPickDenied = "ليس لديك صلاحية اختيار سطر القطعة المكررة";

    private Task<bool> CanPickRowAsync(int userId) => db.AppUsers.AsNoTracking().Where(x => x.Id == userId).Select(x => x.CanPickDuplicateRow).FirstOrDefaultAsync();

    /// <summary>The row a counter without the pick grant counts: the first uncounted row, else the first row. Same order as counting by part number.</summary>
    private static MobilePart DefaultRow(IEnumerable<MobilePart> rows) =>
        rows.OrderBy(x => x.IsCounted ? 1 : 0).ThenBy(x => x.RowNo).First();

    /// <summary>Uppercase and keep only letters/digits so "58310 BWA40", "58310-BWA40" and "58310BWA40" match.</summary>
    public static string NormalizePartCode(string? v) => new string((v ?? "").ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());

    public async Task<MobileResult<MobilePart>> PartAsync(int userId, int id)
    {
        var whs = await MyWarehouses(userId); var brs = await MyBranches(userId);
        var x = await db.InventoryParts.AsNoTracking().FirstOrDefaultAsync(z => z.Id == id);
        if (x == null || !CanSee(x, whs, brs)) return new(null, "القطعة غير موجودة", 404);
        return new(await Project(db.InventoryParts.AsNoTracking().Where(z => z.Id == id), userId).FirstAsync(), null);
    }

    public Task<MobileResult<MobilePart>> CountAsync(int userId, CountInput i) => CountAsync(userId, i, assignedRow: false);

    private async Task<MobileResult<MobilePart>> CountAsync(int userId, CountInput i, bool assignedRow, bool newLine = false)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        if (i.Quantity is null || i.Quantity < 0) return new(null, "الكمية مطلوبة ويجب ألا تكون سالبة", 400);
        if (Validators.GpsError(i.Latitude, i.Longitude) is string g) return new(null, g, 400);
        var user = await db.AppUsers.FirstAsync(x => x.Id == userId);
        if (!CountTypes.Parts(user.CountType)) return new(null, CountTypes.PartsDenied, 403);
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
        // A counted row is counted once. Only the counter who counted it may count it again; everyone else is told who counted it, never the quantity.
        // An admin-assigned recount task (assignedRow) is the only other way back in.
        if (part.CountedQuantity != null && !assignedRow && part.CountedByUserId != userId)
        {
            var by = part.CountedByUserId is int cb ? await db.AppUsers.AsNoTracking().Where(u => u.Id == cb).Select(u => u.Name).FirstOrDefaultAsync() : null;
            return new(null, AlreadyCountedBy(by), 409, new CountedByDetail(by));
        }
        var loc = i.Location?.Trim(); if (loc?.Length > 64) loc = loc[..64];
        // Every count, above or below the system quantity, is saved. The variance shows on the finance page as surplus or shortage; no approval request is created.
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

    public const string AlreadyCounted = "هذه القطعة معدودة سابقًا، يرجى جرد قطعة أخرى";

    /// <summary>"Already counted by employee X". Falls back to <see cref="AlreadyCounted"/> when the counter is unknown.</summary>
    public static string AlreadyCountedBy(string? name) => string.IsNullOrWhiteSpace(name) ? AlreadyCounted : $"هذه القطعة معدودة من قبل الموظف {name}";

    public async Task<MobileResult<MobileVin>> ScanVinAsync(int userId, ScanInput i)
    {
        if (!await AllowsVehiclesAsync(userId)) return new(null, CountTypes.VehiclesDenied, 403);
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
        var already = await ScannerOfAsync(s.Id, vin, userId);
        if (already != null) return new(null, AlreadyScannedBy(already.Value.Name, already.Value.Mine), 409, new ScannedByDetail(already.Value.Name, already.Value.Mine));
        var wh = await db.Warehouses.FirstAsync(x => x.Id == wid);
        var e = new ScannedVin { SessionId = s.Id, Vin = vin, UserId = userId, WarehouseId = wid, BranchId = wh.BranchId, Latitude = i.Latitude, Longitude = i.Longitude };
        db.ScannedVins.Add(e);
        await db.SaveChangesAsync();
        var exp = await db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == s.Id && x.Vin == vin).Select(x => new { x.Model, x.Dealer, x.ManufactureYear }).FirstOrDefaultAsync();
        return new(new MobileVin(e.Id, e.Vin, wid, wh.Name, e.Latitude, e.Longitude, e.CreatedAt, exp != null, exp?.Model, exp?.Dealer, exp?.ManufactureYear), null, 201);
    }

    public const string AlreadyScanned = "رقم VIN ممسوح مسبقًا في هذه الجلسة";

    /// <summary>"Already scanned by employee X", or a note to the scanner themself. Falls back to <see cref="AlreadyScanned"/> when the name is unknown.</summary>
    public static string AlreadyScannedBy(string? name, bool mine) =>
        mine ? "أنت مسحت هذا الشاصي مسبقًا في هذه الجلسة"
        : string.IsNullOrWhiteSpace(name) ? AlreadyScanned : $"هذا الشاصي ممسوح من قبل الموظف {name}";

    /// <summary>Who scanned the VIN in this session, if anyone. Only the name is returned, never the warehouse or location.</summary>
    private async Task<(string? Name, bool Mine)?> ScannerOfAsync(int sessionId, string vin, int userId)
    {
        var row = await db.ScannedVins.AsNoTracking().Where(x => x.SessionId == sessionId && x.Vin == vin)
            .OrderBy(x => x.Id).Select(x => new { x.UserId, Name = x.User != null ? x.User.Name : null }).FirstOrDefaultAsync();
        return row == null ? null : (row.Name, row.UserId == userId);
    }

    /// <summary>Vehicle type for a VIN read by the app (from the session file), before it is saved.</summary>
    public async Task<MobileResult<MobileVinLookup>> LookupVinAsync(int userId, string? raw)
    {
        if (!await AllowsVehiclesAsync(userId)) return new(null, CountTypes.VehiclesDenied, 403);
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        var vin = Validators.NormalizeVin(raw);
        if (!Validators.IsVin(vin)) return new(null, Validators.VinError, 400);
        var exp = await db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == s.Id && x.Vin == vin).Select(x => new { x.Model, x.Dealer, x.ManufactureYear }).FirstOrDefaultAsync();
        var scanner = await ScannerOfAsync(s.Id, vin, userId);
        return new(new MobileVinLookup(vin, exp != null, exp?.Model, scanner != null, exp?.Dealer, exp?.ManufactureYear, scanner?.Name, scanner?.Mine ?? false), null, 200);
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

    private async Task<bool> AllowsVehiclesAsync(int userId) =>
        CountTypes.Vehicles(await db.AppUsers.AsNoTracking().Where(x => x.Id == userId).Select(x => x.CountType).FirstOrDefaultAsync());

    /// <summary>Spare-part flows outside counting (new-part requests) use the same rule.</summary>
    public async Task<bool> AllowsPartsAsync(int userId) =>
        CountTypes.Parts(await db.AppUsers.AsNoTracking().Where(x => x.Id == userId).Select(x => x.CountType).FirstOrDefaultAsync());

    public async Task<MobileResult<MobileVinRequest>> VinCorrectionAsync(int userId, VinCorrectionInput i)
    {
        if (!await AllowsVehiclesAsync(userId)) return new(null, CountTypes.VehiclesDenied, 403);
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
        var counted = r.InventoryPart!;
        var loc = i.Location?.Trim();
        // A different location is a new line of the same part, so the first count stays as it was.
        var newLine = counted.CountedQuantity != null && !string.IsNullOrEmpty(loc) && !string.Equals(counted.CountedLocation ?? "", loc, StringComparison.OrdinalIgnoreCase);
        await using var tx = await db.Database.BeginTransactionAsync();
        var targetId = r.InventoryPartId;
        if (newLine)
        {
            var line = new InventoryPart { SessionId = counted.SessionId, PartNumber = counted.PartNumber, Description = counted.Description, SystemQuantity = 0, SystemLocation = counted.SystemLocation, BranchId = counted.BranchId, WarehouseId = counted.WarehouseId };
            db.InventoryParts.Add(line);
            await db.SaveChangesAsync();
            targetId = line.Id;
        }
        var res = await CountAsync(userId, new CountInput { PartId = targetId, Quantity = i.Quantity, Location = i.Location, Latitude = i.Latitude, Longitude = i.Longitude, Confirm = i.Confirm }, assignedRow: true, newLine: newLine);
        if (res.Error != null) { await tx.RollbackAsync(); return res; }
        r.Status = RecountStatus.Completed; r.ResultQuantity = i.Quantity; r.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return res;
    }

    /// <summary>The counter who counted a part asks to count it again. Only that counter may ask, and an admin must approve.</summary>
    public async Task<MobileResult<MobileRecountAsk>> AskRecountAsync(int userId, int partId)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "لا توجد جلسة جرد قيد التنفيذ", 409);
        var whs = await MyWarehouses(userId); var brs = await MyBranches(userId);
        var part = await db.InventoryParts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == partId && x.SessionId == s.Id);
        if (part == null || !CanSee(part, whs, brs)) return new(null, "القطعة غير موجودة في الجلسة الحالية", 404);
        if (part.CountedQuantity == null) return new(null, "القطعة لم تُعدّ بعد، يمكنك جردها مباشرة", 409);
        if (part.CountedByUserId != userId) return new(null, "فقط من جرد القطعة أول مرة يمكنه طلب إعادة جردها", 403);
        if (await db.RecountAsks.AnyAsync(x => x.InventoryPartId == partId && x.RequestedByUserId == userId && x.Status == RecountAskStatus.Pending))
            return new(null, "طلبك قيد المراجعة", 409);
        if (await db.RecountRequests.AnyAsync(x => x.InventoryPartId == partId && x.AssignedUserId == userId && x.Status == RecountStatus.Pending))
            return new(null, "لديك طلب إعادة عد مفتوح لهذه القطعة", 409);
        var ask = new RecountAsk { InventoryPartId = partId, RequestedByUserId = userId };
        db.RecountAsks.Add(ask);
        await db.SaveChangesAsync();
        return new(new MobileRecountAsk(ask.Id, partId, part.PartNumber, "بانتظار الموافقة", ask.CreatedAt), null, 201, null, "تم إرسال طلب إعادة الجرد، سيصلك إشعار عند الموافقة");
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

/// <summary>Count-type rule shared by the API and the profile.</summary>
public static class CountTypes
{
    public static bool Parts(CountType t) => t is CountType.Parts or CountType.Both;
    public static bool Vehicles(CountType t) => t is CountType.Vehicles or CountType.Both;
    public const string PartsDenied = "حسابك غير مخوّل بجرد قطع الغيار";
    public const string VehiclesDenied = "حسابك غير مخوّل بجرد السيارات";
}
