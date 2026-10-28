using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record MobileWarehouse(int Id, string Name, string Type, int BranchId, string BranchName);
public sealed record MobileProfile(int Id, string Name, string PhoneNumber, List<MobileWarehouse> Warehouses);
public sealed record MobileSession(int Id, int Year, string Name, string Status, DateTime StartedAt);
public sealed record MobilePart(int Id, string PartNumber, string Description, string? SystemLocation, int WarehouseId, string WarehouseName, int? CountedQuantity, string? CountedLocation, DateTime? CountedAt, bool CountedByMe);
public sealed record MobileVin(int Id, string Vin, int WarehouseId, string WarehouseName, double? Latitude, double? Longitude, DateTime CreatedAt, bool IsExpected);
public sealed record MobileRecount(int Id, int PartId, string PartNumber, string Description, string? SystemLocation, string WarehouseName, string RequestedBy, DateTime CreatedAt);
public sealed record MobileVinRequest(int Id, string CurrentVin, string NewVin, string Reason, string Status, DateTime CreatedAt, DateTime? ApprovedAt);
public sealed record HistoryItem(string Type, int Id, string Reference, string? Detail, int WarehouseId, string WarehouseName, DateTime At);

public sealed class CountInput { public int? PartId { get; set; } public string? PartNumber { get; set; } public string? Description { get; set; } public int? WarehouseId { get; set; } public int? Quantity { get; set; } public string? Location { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }
public sealed class ScanInput { public string? Vin { get; set; } public int? WarehouseId { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }
public sealed class VinCorrectionInput { public int? ScannedVinId { get; set; } public string? NewVin { get; set; } public string? Reason { get; set; } }
public sealed class RecountInput { public int? Quantity { get; set; } public string? Location { get; set; } public double? Latitude { get; set; } public double? Longitude { get; set; } }

public sealed record MobileResult<T>(T? Data, string? Error, int Status = 200);

public sealed class MobileService(AppDbContext db, SessionLookup sessions, IPasswordService pw)
{
    public async Task<AppUser?> LoginAsync(string? phone, string? password)
    {
        phone = phone?.Trim();
        if (string.IsNullOrEmpty(phone) || string.IsNullOrEmpty(password)) return null;
        var u = await db.AppUsers.FirstOrDefaultAsync(x => x.PhoneNumber == phone);
        if (u == null || !u.IsActive || !pw.Verify(u.PasswordHash, password)) return null;
        return u;
    }

    public Task<bool> IsActiveAsync(int userId) => db.AppUsers.AnyAsync(x => x.Id == userId && x.IsActive);

    private Task<List<int>> MyWarehouses(int userId) => db.AppUserWarehouses.Where(x => x.AppUserId == userId).Select(x => x.WarehouseId).ToListAsync();

    public async Task<MobileProfile?> ProfileAsync(int userId)
    {
        var u = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
        if (u == null) return null;
        var whs = await db.AppUserWarehouses.Where(x => x.AppUserId == userId)
            .Select(x => new MobileWarehouse(x.WarehouseId, x.Warehouse!.Name, x.Warehouse.Type == WarehouseType.Type1 ? "نوع 1" : "نوع 2", x.Warehouse.BranchId, x.Warehouse.Branch!.Name)).ToListAsync();
        return new MobileProfile(u.Id, u.Name, u.PhoneNumber, whs);
    }

    public async Task<MobileSession?> CurrentSessionAsync()
    {
        var s = await sessions.CurrentAsync();
        return s == null ? null : new MobileSession(s.Id, s.Year, $"جلسة جرد {s.Year}", Labels.Session(s.Status), s.StartedAt);
    }

    public async Task<MobileResult<PagedResult<MobilePart>>> PartsAsync(int userId, PageQuery p, int? warehouseId, bool? counted)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "ماكو جلسة جرد قيد التنفيذ", 409);
        var whs = await MyWarehouses(userId);
        if (warehouseId is int w && !whs.Contains(w)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        var q = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == s.Id && x.WarehouseId != null && whs.Contains(x.WarehouseId.Value));
        if (warehouseId is int ww) q = q.Where(x => x.WarehouseId == ww);
        if (counted == true) q = q.Where(x => x.CountedQuantity != null);
        if (counted == false) q = q.Where(x => x.CountedQuantity == null);
        if (p.Term is string t) q = q.Where(x => x.PartNumber.Contains(t) || x.Description.Contains(t));
        var rows = q.OrderBy(x => x.PartNumber).ThenBy(x => x.Id).Select(x => new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId!.Value, x.Warehouse!.Name,
            x.CountedQuantity, x.CountedLocation, x.CountedAt, x.CountedByUserId == userId));
        return new(Paging.Build(rows, p), null);
    }

    public async Task<MobileResult<MobilePart>> PartAsync(int userId, int id)
    {
        var whs = await MyWarehouses(userId);
        var x = await db.InventoryParts.AsNoTracking().Include(z => z.Warehouse).FirstOrDefaultAsync(z => z.Id == id && z.WarehouseId != null && whs.Contains(z.WarehouseId.Value));
        if (x == null) return new(null, "القطعة غير موجودة", 404);
        return new(new MobilePart(x.Id, x.PartNumber, x.Description, x.SystemLocation, x.WarehouseId!.Value, x.Warehouse!.Name, x.CountedQuantity, x.CountedLocation, x.CountedAt, x.CountedByUserId == userId), null);
    }

    private static string? ValidGps(double? lat, double? lng)
    {
        if (lat is null || lng is null) return "الموقع (GPS) مطلوب";
        if (lat is < -90 or > 90 || lng is < -180 or > 180) return "إحداثيات الموقع غير صالحة";
        return null;
    }

    public async Task<MobileResult<MobilePart>> CountAsync(int userId, CountInput i)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "ماكو جلسة جرد قيد التنفيذ", 409);
        if (i.Quantity is null || i.Quantity < 0) return new(null, "الكمية مطلوبة وما تكون سالبة", 400);
        if (ValidGps(i.Latitude, i.Longitude) is string g) return new(null, g, 400);
        var user = await db.AppUsers.FirstAsync(x => x.Id == userId);
        var whs = await MyWarehouses(userId);
        InventoryPart? part;
        if (i.PartId is int pid)
        {
            part = await db.InventoryParts.FirstOrDefaultAsync(x => x.Id == pid && x.SessionId == s.Id);
            if (part == null) return new(null, "القطعة غير موجودة بالجلسة الحالية", 404);
        }
        else
        {
            var pn = i.PartNumber?.Trim();
            if (string.IsNullOrEmpty(pn) || pn.Length > 64) return new(null, "رقم القطعة أو partId مطلوب", 400);
            if (i.WarehouseId is not int wid) return new(null, "المستودع مطلوب", 400);
            part = await db.InventoryParts.FirstOrDefaultAsync(x => x.SessionId == s.Id && x.PartNumber == pn && x.WarehouseId == wid);
            if (part == null)
            {
                if (!whs.Contains(wid)) return new(null, "المستودع غير مرتبط بحسابك", 403);
                var wh = await db.Warehouses.FirstAsync(x => x.Id == wid);
                var d = i.Description?.Trim() ?? "";
                part = new InventoryPart { SessionId = s.Id, PartNumber = pn, Description = d.Length > 400 ? d[..400] : d, SystemQuantity = 0, WarehouseId = wid, BranchId = wh.BranchId };
                db.InventoryParts.Add(part);
            }
        }
        if (part.WarehouseId is not int pw2 || !whs.Contains(pw2)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        var loc = i.Location?.Trim(); if (loc?.Length > 64) loc = loc[..64];
        if (part.Id != 0)
        {
            if (part.CountedQuantity != i.Quantity)
                db.PartChangeLogs.Add(new PartChangeLog { InventoryPartId = part.Id, Field = "الكمية المحسوبة", OldValue = part.CountedQuantity?.ToString(), NewValue = i.Quantity.ToString(), ChangedBy = user.Name, Source = "التطبيق" });
            if ((part.CountedLocation ?? "") != (loc ?? ""))
                db.PartChangeLogs.Add(new PartChangeLog { InventoryPartId = part.Id, Field = "الموقع المحسوب", OldValue = part.CountedLocation, NewValue = loc, ChangedBy = user.Name, Source = "التطبيق" });
        }
        else part.ChangeLogs.Add(new PartChangeLog { Field = "الكمية المحسوبة", NewValue = i.Quantity.ToString(), ChangedBy = user.Name, Source = "التطبيق (قطعة غير مدرجة)" });
        part.CountedQuantity = i.Quantity; part.CountedLocation = loc; part.CountedByUserId = userId; part.CountedAt = DateTime.UtcNow;
        part.Latitude = i.Latitude; part.Longitude = i.Longitude;
        await db.SaveChangesAsync();
        return await PartAsync(userId, part.Id);
    }

    public async Task<MobileResult<MobileVin>> ScanVinAsync(int userId, ScanInput i)
    {
        var s = await sessions.CurrentAsync();
        if (s == null) return new(null, "ماكو جلسة جرد قيد التنفيذ", 409);
        var vin = Validators.NormalizeVin(i.Vin);
        if (!Validators.IsVin(vin)) return new(null, Validators.VinError, 400);
        if (ValidGps(i.Latitude, i.Longitude) is string g) return new(null, g, 400);
        if (i.WarehouseId is not int wid) return new(null, "المستودع مطلوب", 400);
        var whs = await MyWarehouses(userId);
        if (!whs.Contains(wid)) return new(null, "المستودع غير مرتبط بحسابك", 403);
        if (await db.ScannedVins.AnyAsync(x => x.SessionId == s.Id && x.Vin == vin)) return new(null, "رقم VIN ممسوح مسبقاً بهذه الجلسة", 409);
        var wh = await db.Warehouses.FirstAsync(x => x.Id == wid);
        var e = new ScannedVin { SessionId = s.Id, Vin = vin, UserId = userId, WarehouseId = wid, BranchId = wh.BranchId, Latitude = i.Latitude, Longitude = i.Longitude };
        db.ScannedVins.Add(e);
        await db.SaveChangesAsync();
        var expected = await db.ExpectedVins.AnyAsync(x => x.SessionId == s.Id && x.Vin == vin);
        return new(new MobileVin(e.Id, e.Vin, wid, wh.Name, e.Latitude, e.Longitude, e.CreatedAt, expected), null, 201);
    }

    public async Task<PagedResult<MobileVin>> MyVinsAsync(int userId, PageQuery p)
    {
        var s = await sessions.CurrentAsync();
        var sid = s?.Id ?? -1;
        var q = db.ScannedVins.AsNoTracking().Where(x => x.UserId == userId && x.SessionId == sid);
        if (p.Term is string t) q = q.Where(x => x.Vin.Contains(t));
        var rows = q.OrderByDescending(x => x.CreatedAt).Select(x => new MobileVin(x.Id, x.Vin, x.WarehouseId, x.Warehouse!.Name, x.Latitude, x.Longitude, x.CreatedAt,
            db.ExpectedVins.Any(e => e.SessionId == x.SessionId && e.Vin == x.Vin)));
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
        if (v.Vin == newVin) return new(null, "رقم VIN الجديد نفس الحالي", 400);
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

    public Task<PagedResult<MobileRecount>> RecountsAsync(int userId, PageQuery p)
    {
        var q = db.RecountRequests.AsNoTracking().Where(x => x.AssignedUserId == userId && x.Status == RecountStatus.Pending && x.InventoryPart!.Session!.Status == SessionStatus.InProgress)
            .OrderBy(x => x.CreatedAt).Select(x => new MobileRecount(x.Id, x.InventoryPartId, x.InventoryPart!.PartNumber, x.InventoryPart.Description, x.InventoryPart.SystemLocation,
                x.InventoryPart.Warehouse != null ? x.InventoryPart.Warehouse.Name : "", x.RequestedByAdmin!.Name, x.CreatedAt));
        return Task.FromResult(Paging.Build(q, p));
    }

    public async Task<MobileResult<MobilePart>> SubmitRecountAsync(int userId, int id, RecountInput i)
    {
        var r = await db.RecountRequests.Include(x => x.InventoryPart).ThenInclude(p => p!.Session).FirstOrDefaultAsync(x => x.Id == id && x.AssignedUserId == userId);
        if (r == null) return new(null, "طلب إعادة العد غير موجود", 404);
        if (r.Status != RecountStatus.Pending) return new(null, "طلب إعادة العد مكتمل", 409);
        if (r.InventoryPart!.Session!.Status != SessionStatus.InProgress) return new(null, "جلسة الجرد منتهية", 409);
        var res = await CountAsync(userId, new CountInput { PartId = r.InventoryPartId, Quantity = i.Quantity, Location = i.Location, Latitude = i.Latitude, Longitude = i.Longitude });
        if (res.Error != null) return res;
        r.Status = RecountStatus.Completed; r.ResultQuantity = i.Quantity; r.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return res;
    }

    public async Task<PagedResult<HistoryItem>> HistoryAsync(int userId, PageQuery p, string? type)
    {
        var s = await sessions.CurrentAsync();
        var sid = s?.Id ?? -1;
        var parts = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid && x.CountedByUserId == userId && x.CountedAt != null)
            .Select(x => new HistoryItem("part", x.Id, x.PartNumber, x.CountedQuantity.ToString(), x.WarehouseId!.Value, x.Warehouse!.Name, x.CountedAt!.Value));
        var vins = db.ScannedVins.AsNoTracking().Where(x => x.SessionId == sid && x.UserId == userId)
            .Select(x => new HistoryItem("vin", x.Id, x.Vin, null, x.WarehouseId, x.Warehouse!.Name, x.CreatedAt));
        if (p.Term is string t) { parts = parts.Where(x => x.Reference.Contains(t)); vins = vins.Where(x => x.Reference.Contains(t)); }
        var all = new List<HistoryItem>();
        if (type != "vin") all.AddRange(await parts.ToListAsync());
        if (type != "part") all.AddRange(await vins.ToListAsync());
        all = all.OrderByDescending(x => x.At).ToList();
        var page = p.SafePage; var size = p.SafeSize;
        return new PagedResult<HistoryItem> { Items = all.Skip((page - 1) * size).Take(size).ToList(), Page = page, PageSize = size, TotalCount = all.Count };
    }
}
