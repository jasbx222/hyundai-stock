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
    public int SafeSize => PageSize is < 1 or > 100 ? 10 : PageSize;
    public string? Term => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}

public sealed record OpResult(bool Ok, string? Error = null, int? Id = null)
{
    public static OpResult Success(int? id = null) => new(true, null, id);
    public static OpResult Fail(string error) => new(false, error);
}

/// <summary>The signed-in dashboard account and its data scope.</summary>
public sealed record AdminScope(int Id, string Name, AdminRole Role, int? BranchId)
{
    public bool IsSuper => Role is AdminRole.MainAdmin or AdminRole.SuperAdmin;
    public bool IsMain => Role == AdminRole.MainAdmin;
    /// <summary>Branch filter: null means all branches.</summary>
    public int? ScopeBranch => IsSuper ? null : (BranchId ?? -1);
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
        var items = q.Skip((page - 1) * size).Take(size).ToList();
        return new PagedResult<T> { Items = items, Page = page, PageSize = size, TotalCount = total };
    }
}
