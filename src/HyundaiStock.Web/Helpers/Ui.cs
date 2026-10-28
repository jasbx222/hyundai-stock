using HyundaiStock.Domain;
using Microsoft.AspNetCore.Html;

namespace HyundaiStock.Web.Helpers;

public static class Ui
{
    private static readonly TimeZoneInfo Tz = FindTz();
    private static TimeZoneInfo FindTz()
    {
        foreach (var id in new[] { "Asia/Baghdad", "Arabic Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch { }
        return TimeZoneInfo.CreateCustomTimeZone("Baghdad", TimeSpan.FromHours(3), "Baghdad", "Baghdad");
    }

    public static System.DateTime Local(System.DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(System.DateTime.SpecifyKind(utc, DateTimeKind.Utc), Tz);
    public static string Date(System.DateTime? utc) => utc is System.DateTime d ? Local(d).ToString("yyyy/MM/dd") : "—";
    public static string Time(System.DateTime? utc)
    {
        if (utc is not System.DateTime d) return "—";
        var l = Local(d);
        return l.ToString("hh:mm") + (l.Hour < 12 ? " ص" : " م");
    }
    public static string DT(System.DateTime? utc) => utc is System.DateTime d ? $"{Date(d)} {Time(d)}" : "—";

    public static IHtmlContent Icon(string name, int size = 18) =>
        new HtmlString($"<svg class=\"ic\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{(IconSet.Paths.TryGetValue(name, out var p) ? p : "")}</svg>");

    public static IHtmlContent Badge(string text, string cls) => new HtmlString($"<span class=\"bd {cls}\">{System.Net.WebUtility.HtmlEncode(text)}</span>");

    public static IHtmlContent Active(bool a) => Badge(a ? "نشط" : "غير نشط", a ? "ok" : "er");
    public static IHtmlContent Qty(RequestStatus s, ApprovalLevel l) => s switch
    {
        RequestStatus.Approved => Badge("مقبول", "ok"),
        RequestStatus.Rejected => Badge("مرفوض", "er"),
        _ => Badge(l == ApprovalLevel.First ? "معلق" : "قيد الانتظار", "wa")
    };
    public static IHtmlContent Vin(RequestStatus s) => Badge(Labels.Vin(s), s == RequestStatus.Approved ? "ok" : s == RequestStatus.Rejected ? "er" : "wa");
    public static IHtmlContent Session(SessionStatus s) => Badge(Labels.Session(s), s == SessionStatus.Completed ? "ok" : "wa");
    public static IHtmlContent Recount(RecountStatus s) => Badge(Labels.Recount(s), s == RecountStatus.Completed ? "ok" : "wa");

    public static IHtmlContent Diff(int? counted, int system)
    {
        if (counted is not int c) return new HtmlString("<span class=\"muted\">—</span>");
        var d = c - system;
        var cls = d < 0 ? "n" : d > 0 ? "p" : "z";
        return new HtmlString($"<span class=\"diff {cls}\">{(d > 0 ? "+" : "")}{d}</span>");
    }

    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "؟" : name.Trim()[..1];
    public static string Idx(int id) => id.ToString("00");
}
