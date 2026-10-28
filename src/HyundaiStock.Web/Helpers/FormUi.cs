using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HyundaiStock.Web.Helpers;

public static class FormUi
{
    public static string? ErrorOf(this ModelStateDictionary ms, string key)
        => ms.TryGetValue(key, out var e) && e.Errors.Count > 0 ? e.Errors[0].ErrorMessage : null;
    public static IHtmlContent Err(this ModelStateDictionary ms, string key)
        => ms.ErrorOf(key) is string m ? new HtmlString($"<span class=\"hint e\" role=\"alert\">{System.Net.WebUtility.HtmlEncode(m)}</span>") : HtmlString.Empty;
    public static string Cls(this ModelStateDictionary ms, string key) => ms.ErrorOf(key) != null ? "field err" : "field";
}
