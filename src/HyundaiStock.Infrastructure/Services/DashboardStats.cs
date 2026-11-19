using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record ActivityItem(string Kind, string Title, string Detail, string By, DateTime At);
public sealed record BranchProgress(string Name, int Parts, int Counted, int Vins, int Scanned);

/// <summary>Home page summary for the current (or latest) session, limited to the admin's branch when not super admin.</summary>
public sealed class HomeStats
{
    public int? SessionYear { get; init; }
    public string? SessionName { get; init; }
    public bool SessionOpen { get; init; }
    public DateTime? SessionStartedAt { get; init; }
    public int Sessions { get; init; }
    public int Branches { get; init; }
    public int Warehouses { get; init; }
    public int Users { get; init; }
    public int Parts { get; init; }
    public int CountedParts { get; init; }
    public int MatchedParts { get; init; }
    public int Shortage { get; init; }
    public int Surplus { get; init; }
    public int ExpectedVins { get; init; }
    public int ScannedVins { get; init; }
    public int ScannedExpected { get; init; }
    public int PendingVin { get; init; }
    public int PendingRecounts { get; init; }
    public int Recounts { get; init; }
    public List<BranchProgress> ByBranch { get; init; } = new();
    public List<ActivityItem> Recent { get; init; } = new();

    public int CountedPercent => Parts == 0 ? 0 : (int)Math.Round(CountedParts * 100.0 / Parts);
    public int VinPercent => ExpectedVins == 0 ? 0 : (int)Math.Round(ScannedExpected * 100.0 / ExpectedVins);

    public static async Task<HomeStats> BuildAsync(AppDbContext db, SessionLookup sessions, AdminScope s, int? sessionId = null)
    {
        var all = s.IsUnrestricted;
        var wm = s.Role == AdminRole.WarehouseManager;
        var branchIds = s.Branches.ToList();
        var whIds = s.Warehouses.ToList();
        var cur = sessionId is int sel ? await sessions.ForSessionAsync(sel) : await sessions.CurrentAsync() ?? await sessions.LatestAsync();
        var sid = cur?.Id ?? -1;
        var parts = db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sid);
        if (!all) parts = wm ? parts.Where(x => x.WarehouseId != null && whIds.Contains(x.WarehouseId.Value)) : parts.Where(x => x.BranchId != null && branchIds.Contains(x.BranchId.Value));
        var counted = parts.Where(x => x.CountedQuantity != null);
        var hideVehicles = s.Role == AdminRole.SparePartsManager;
        var exp = db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sid);
        var scan = db.ScannedVins.AsNoTracking().Where(x => x.SessionId == sid);
        if (!all && !wm) { exp = exp.Where(x => x.BranchId != null && branchIds.Contains(x.BranchId.Value)); scan = scan.Where(x => branchIds.Contains(x.BranchId)); }
        if (wm) scan = scan.Where(x => whIds.Contains(x.WarehouseId));

        var branches = await db.Branches.AsNoTracking().Where(x => all || branchIds.Contains(x.Id) || (wm && x.Warehouses.Any(w => whIds.Contains(w.Id)))).Select(x => new { x.Id, x.Name }).ToListAsync();
        var partBy = await parts.GroupBy(x => x.BranchId).Select(g => new { g.Key, N = g.Count(), C = g.Count(x => x.CountedQuantity != null) }).ToListAsync();
        var expBy = hideVehicles ? new Dictionary<int, int>() : await exp.GroupBy(x => x.BranchId).Select(g => new { g.Key, N = g.Count() }).Where(x => x.Key != null).ToDictionaryAsync(x => x.Key!.Value, x => x.N);
        var scanBy = hideVehicles ? new Dictionary<int, int>() : await scan.GroupBy(x => x.BranchId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);

        var recent = new List<ActivityItem>();
        recent.AddRange(await counted.Where(x => x.CountedAt != null).OrderByDescending(x => x.CountedAt).Take(6)
            .Select(x => new ActivityItem("part", "عدّ قطعة " + x.PartNumber, x.Description, x.CountedByUser != null ? x.CountedByUser.Name : "", x.CountedAt ?? default)).ToListAsync());
        if (!hideVehicles)
            recent.AddRange(await scan.OrderByDescending(x => x.CreatedAt).Take(6)
                .Select(x => new ActivityItem("car", "مسح مركبة " + x.Vin, x.Warehouse != null ? x.Warehouse.Name : "", x.User != null ? x.User.Name : "", x.CreatedAt)).ToListAsync());

        return new HomeStats
        {
            SessionYear = cur?.Year,
            SessionName = cur?.Name,
            SessionOpen = cur?.Status == SessionStatus.InProgress,
            SessionStartedAt = cur?.StartedAt,
            Sessions = await db.Sessions.CountAsync(),
            Branches = branches.Count,
            Warehouses = await db.Warehouses.CountAsync(x => all || (wm ? whIds.Contains(x.Id) : branchIds.Contains(x.BranchId))),
            Users = await db.AppUsers.CountAsync(),
            Parts = await parts.CountAsync(),
            CountedParts = await counted.CountAsync(),
            MatchedParts = await counted.CountAsync(x => x.CountedQuantity == x.SystemQuantity),
            Shortage = await counted.CountAsync(x => x.CountedQuantity < x.SystemQuantity),
            Surplus = await counted.CountAsync(x => x.CountedQuantity > x.SystemQuantity),
            ExpectedVins = hideVehicles ? 0 : await exp.CountAsync(),
            ScannedVins = hideVehicles ? 0 : await scan.CountAsync(),
            ScannedExpected = hideVehicles ? 0 : await scan.CountAsync(x => db.ExpectedVins.Any(e => e.SessionId == x.SessionId && e.Vin == x.Vin)),
            PendingVin = await db.VinCorrections.CountAsync(x => x.Status == RequestStatus.Pending && (all || (wm ? whIds.Contains(x.ScannedVin!.WarehouseId) : branchIds.Contains(x.ScannedVin!.BranchId)))),
            PendingRecounts = await db.RecountRequests.CountAsync(x => x.Status == RecountStatus.Pending && (all || (wm ? x.InventoryPart!.WarehouseId != null && whIds.Contains(x.InventoryPart.WarehouseId.Value) : x.InventoryPart!.BranchId != null && branchIds.Contains(x.InventoryPart.BranchId.Value)))),
            Recounts = await db.RecountRequests.CountAsync(x => all || (wm ? x.InventoryPart!.WarehouseId != null && whIds.Contains(x.InventoryPart.WarehouseId.Value) : x.InventoryPart!.BranchId != null && branchIds.Contains(x.InventoryPart.BranchId.Value))),
            ByBranch = branches.Select(x => new BranchProgress(x.Name,
                partBy.FirstOrDefault(p => p.Key == x.Id)?.N ?? 0, partBy.FirstOrDefault(p => p.Key == x.Id)?.C ?? 0,
                expBy.GetValueOrDefault(x.Id), scanBy.GetValueOrDefault(x.Id)))
                .OrderByDescending(x => x.Parts + x.Vins).ToList(),
            Recent = recent.OrderByDescending(x => x.At).Take(8).ToList(),
        };
    }
}
