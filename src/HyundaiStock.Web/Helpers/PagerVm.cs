namespace HyundaiStock.Web.Helpers;
public sealed record PagerVm(int Page, int TotalPages, int From, int To, int Total)
{
    public static PagerVm Of<T>(HyundaiStock.Infrastructure.Common.PagedResult<T> p) => new(p.Page, p.TotalPages, p.From, p.To, p.TotalCount);
}
public sealed record EmptyVm(string Title, string Text, string Icon = "box");
