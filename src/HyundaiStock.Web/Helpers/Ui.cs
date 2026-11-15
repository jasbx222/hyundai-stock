using HyundaiStock.Domain;
using System.Globalization;
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
    public static string Date(System.DateTime? utc) => utc is System.DateTime d ? Local(d).ToString("yyyy'/'MM'/'dd", CultureInfo.InvariantCulture) : "—";
    public static string Time(System.DateTime? utc)
    {
        if (utc is not System.DateTime d) return "—";
        var l = Local(d);
        return l.ToString("hh':'mm", CultureInfo.InvariantCulture) + (l.Hour < 12 ? " ص" : " م");
    }
    public static string DT(System.DateTime? utc) => utc is System.DateTime d ? $"{Date(d)} {Time(d)}" : "—";
    /// <summary>Date/time rendered per the design: Mono, isolated LTR, e.g. 2026/10/28 01:24 م.</summary>
    public static IHtmlContent When(System.DateTime? utc) => Mono(DT(utc));
    public static IHtmlContent Day(System.DateTime? utc) => Mono(Date(utc));
    public static IHtmlContent Clock(System.DateTime? utc) => Mono(Time(utc));
    private static IHtmlContent Mono(string v) => v == "—" ? new HtmlString("<span class=\"muted\">—</span>") : new HtmlString($"<span class=\"dt\" dir=\"ltr\">{v}</span>");

    public static IHtmlContent Icon(string name, int size = 18) =>
        new HtmlString($"<svg class=\"ic\" width=\"{size}\" height=\"{size}\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">{(IconSet.Paths.TryGetValue(name, out var p) ? p : "")}</svg>");

    public static IHtmlContent Badge(string text, string cls) => new HtmlString($"<span class=\"bd {cls}\">{System.Net.WebUtility.HtmlEncode(text)}</span>");

    public static IHtmlContent Active(bool a) => Badge(a ? "نشط" : "معطّل", a ? "ok" : "er");
    public static IHtmlContent Qty(RequestStatus s, ApprovalLevel l) => s switch
    {
        RequestStatus.Approved => Badge("مقبول", "ok"),
        RequestStatus.Rejected => Badge("مرفوض", "er"),
        _ => Badge(l == ApprovalLevel.First ? "بانتظار المالية" : "بانتظار مدير المخزن", "wa")
    };
    public static IHtmlContent Vin(RequestStatus s) => Badge(Labels.Vin(s), s == RequestStatus.Approved ? "ok" : s == RequestStatus.Rejected ? "er" : "wa");
    public static IHtmlContent Session(SessionStatus s) => Badge(Labels.Session(s), s == SessionStatus.Completed ? "ok" : "wa");
    public static IHtmlContent Recount(RecountStatus s) => Badge(Labels.Recount(s), s == RecountStatus.Completed ? "ok" : "wa");

    public static IHtmlContent Diff(int? counted, int system)
    {
        if (counted is not int c) return new HtmlString("<span class=\"muted\">—</span>");
        var d = c - system;
        var cls = d < 0 ? "n" : d > 0 ? "p" : "z";
        return new HtmlString($"<span class=\"bd nd dfb {cls}\" dir=\"ltr\">{(d > 0 ? "+" : "")}{d}</span>");
    }

    /// <summary>Match state of a part: counted equals system quantity or not.</summary>
    public static IHtmlContent Match(int? counted, int system) => counted is not int c ? Badge("غير معدود", "mu") : c == system ? Badge("متطابق", "ok") : Badge("غير متطابق", "er");
    public static IHtmlContent Role(AdminRole r) => Badge(Labels.Role(r), r == AdminRole.MainAdmin ? "rl1" : r == AdminRole.SuperAdmin ? "rl2" : "rl3");
    /// <summary>Approval step shown in the requests tables (always has a value).</summary>
    public static string Step(RequestStatus s, ApprovalLevel l) => s == RequestStatus.Pending ? (l == ApprovalLevel.First ? "القبول النهائي" : "القبول") : (l == ApprovalLevel.None ? "القبول" : "القبول النهائي");
    public static IHtmlContent Who(string? name) => string.IsNullOrWhiteSpace(name) ? new HtmlString("<span class=\"muted\">—</span>") :
        new HtmlString($"<div class=\"who\"><div class=\"av\">{System.Net.WebUtility.HtmlEncode(Initial(name))}</div><span>{System.Net.WebUtility.HtmlEncode(name)}</span></div>");
    public static string Initial(string? name) => string.IsNullOrWhiteSpace(name) ? "؟" : name.Trim()[..1];
    public static string Idx(int id) => id.ToString("00");
    /// <summary>Carries the page's current filter into an export link: "&key=value" for each non-empty key.</summary>
    public static string FilterQuery(Microsoft.AspNetCore.Http.HttpRequest r, params string[] keys) =>
        string.Concat(keys.Where(k => !string.IsNullOrWhiteSpace(r.Query[k])).Select(k => "&" + k + "=" + Uri.EscapeDataString(r.Query[k].ToString())));
}
