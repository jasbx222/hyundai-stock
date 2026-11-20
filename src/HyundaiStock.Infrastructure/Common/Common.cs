using System.Text.RegularExpressions;
using HyundaiStock.Domain;

namespace HyundaiStock.Infrastructure.Common;

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public int From => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;
    public int To => Math.Min(Page * PageSize, TotalCount);
}

public sealed record PageQuery(int Page = 1, int PageSize = 10, string? Search = null)
{
    public int SafePage => Page < 1 ? 1 : Page;
    /// <summary>Returns every row of the filter on one page. Used by the Excel exports so a file matches the page.</summary>
    public bool Unpaged { get; init; }
    public int SafeSize => Unpaged ? int.MaxValue : PageSize is < 1 or > 100 ? 10 : PageSize;
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

public sealed record OpResult(bool Ok, string? Error = null, int? Id = null)
{
    public static OpResult Success(int? id = null) => new(true, null, id);
    public static OpResult Fail(string error) => new(false, error);
}

/// <summary>The signed-in dashboard account and its data scope. Assignment lists come from the database, never from the client.</summary>
public sealed record AdminScope(int Id, string Name, AdminRole Role, int? BranchId, IReadOnlyList<int>? AssignedWarehouses = null, IReadOnlyList<int>? AssignedBranches = null, bool AllBranches = false)
{
    public bool IsSuper => Role is AdminRole.MainAdmin or AdminRole.SuperAdmin;
    public bool IsMain => Role == AdminRole.MainAdmin;
    /// <summary>Only the accounts manager opens and closes a stocktaking session. Main and super admin may still view sessions and download their files.</summary>
    public bool CanStartSession => Role == AdminRole.AccountsManager;
    public bool CanEndSession => Role == AdminRole.AccountsManager;
    public bool CanViewSessions => IsSuper || Role == AdminRole.AccountsManager;
    /// <summary>All Branches is an explicit flag. A null BranchId never grants it, and it does not grant session or admin management.</summary>
    public bool SeesAllBranches => IsSuper || (AllBranches && Role is AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.Accountant or AdminRole.AccountsManager or AdminRole.VehicleWarehouseManager);
    public bool IsUnrestricted => SeesAllBranches;
    public bool HidesStockFigures => Role is AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager;
    /// <summary>Branch filter: null means every branch. A missing branch stays -1 so it cannot match real rows.</summary>
    public int? ScopeBranch => SeesAllBranches ? null : (BranchId ?? -1);
    public IReadOnlyList<int> Warehouses => AssignedWarehouses ?? Array.Empty<int>();
    public IReadOnlyList<int> Branches => Role switch
    {
        AdminRole.Accountant or AdminRole.AccountsManager => AssignedBranches ?? Array.Empty<int>(),
        AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager => BranchId is int b ? new[] { b } : Array.Empty<int>(),
        _ => Array.Empty<int>()
    };

    public bool CanRequestRecount => Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Admin or AdminRole.WarehouseManager or AdminRole.Accountant or AdminRole.AccountsManager;
    public bool CanReviewNewPart => Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.SparePartsManager;
    /// <summary>Accounts give the final approval on a new-part request after the spare-parts manager.</summary>
    public bool CanFinalApproveNewPart => Role is AdminRole.MainAdmin or AdminRole.SuperAdmin or AdminRole.Accountant or AdminRole.AccountsManager;

    public bool AllowsBranch(int? branchId)
    {
        if (IsUnrestricted) return true;
        if (Role == AdminRole.WarehouseManager) return false;
        return branchId is int b && Branches.Contains(b);
    }

    public bool AllowsWarehouse(int? warehouseId) =>
        Role == AdminRole.WarehouseManager && warehouseId is int w && Warehouses.Contains(w);

    /// <summary>Warehouse managers are limited to assigned warehouses. Other restricted roles are limited to their branches.</summary>
    public bool AllowsInventory(int? warehouseId, int? branchId)
    {
        if (IsUnrestricted) return true;
        if (Role == AdminRole.WarehouseManager) return AllowsWarehouse(warehouseId);
        return AllowsBranch(branchId);
    }
}

public static class Validators
{
    private static readonly Regex Phone = new(@"^07[5-8][0-9]{8}$", RegexOptions.Compiled);
    public static bool IsIraqiPhone(string? v) => v != null && Phone.IsMatch(v.Trim());
    public const string PhoneError = "يجب أن يتكون رقم الهاتف من 11 رقمًا وأن يبدأ بـ 075 أو 076 أو 077 أو 078";
    public static string NormalizeVin(string? v) => (v ?? "").Trim().ToUpperInvariant();
    private static readonly Regex VinRx = new(@"^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.Compiled);
    public static bool IsVin(string? v) => v != null && VinRx.IsMatch(NormalizeVin(v));
    public const string VinError = "يجب أن يتكون رقم VIN من 17 حرفًا ورقمًا (بدون I و O و Q)";
    public static bool HasMarkup(string? v) => !string.IsNullOrEmpty(v) && (v.Contains('<') || v.Contains('>'));
    public const string MarkupError = "النص يحتوي على رموز غير مسموحة";
    public static string? GpsError(double? lat, double? lng)
    {
        if (lat is null || lng is null || double.IsNaN(lat.Value) || double.IsNaN(lng.Value) || double.IsInfinity(lat.Value) || double.IsInfinity(lng.Value))
            return "تعذر تحديد موقعك، يرجى المحاولة مرة أخرى";
        if (lat is < -90 or > 90 || lng is < -180 or > 180 || (lat == 0 && lng == 0))
            return "تعذر تحديد موقعك، يرجى المحاولة مرة أخرى";
        return null;
    }
}

public static class Paging
{
    public static PagedResult<T> Build<T>(IQueryable<T> q, PageQuery p) where T : class
    {
        var total = q.Count();
        var page = p.SafePage;
        var size = p.SafeSize;
        var maxPage = Math.Max(1, (int)Math.Ceiling(total / (double)size));
        if (page > maxPage) page = maxPage;
        var items = (size == int.MaxValue ? q : q.Skip((page - 1) * size).Take(size)).ToList();
        return new PagedResult<T> { Items = items, Page = page, PageSize = size, TotalCount = total };
    }
}
