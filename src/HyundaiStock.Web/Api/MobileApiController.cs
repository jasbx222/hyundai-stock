using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HyundaiStock.Web.Api;

public sealed class LoginRequest
{
    /// <summary>رقم الهاتف (07XXXXXXXXX)</summary>
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
}

/// <summary>Mobile app API for field counters. All responses use the <see cref="ApiResponse"/> envelope.</summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class MobileApiController(MobileService svc, TokenService tokens) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst("sub")!.Value);

    private IActionResult From<T>(MobileResult<T> r, string? okMessage = null) =>
        r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : StatusCode(r.Status, ApiResponse.Ok(r.Data, okMessage));

    /// <summary>تسجيل دخول مستخدم التطبيق برقم الهاتف وكلمة المرور، ويرجع JWT.</summary>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 401)]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.PhoneNumber) || string.IsNullOrEmpty(req.Password))
            return BadRequest(ApiResponse.Fail("رقم الهاتف وكلمة المرور مطلوبة"));
        var u = await svc.LoginAsync(req.PhoneNumber, req.Password);
        if (u == null) return Unauthorized(ApiResponse.Fail("رقم الهاتف أو كلمة المرور غير صحيحة أو الحساب معطّل"));
        var (token, exp) = tokens.Create(u);
        return Ok(ApiResponse.Ok(new TokenDto(token, "Bearer", exp, await svc.ProfileAsync(u.Id) ?? (object)new { })));
    }

    /// <summary>بيانات المستخدم الحالي ومستودعاته.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(ApiResponse.Ok(await svc.ProfileAsync(UserId)));

    /// <summary>جلسة الجرد الحالية (قيد التنفيذ)، أو null.</summary>
    [HttpGet("sessions/current")]
    public async Task<IActionResult> CurrentSession()
    {
        var s = await svc.CurrentSessionAsync();
        return Ok(ApiResponse.Ok(s, s == null ? "ماكو جلسة جرد قيد التنفيذ" : null));
    }

    /// <summary>قطع الغيار بالجلسة الحالية ضمن مستودعات المستخدم، مع بحث وترقيم.</summary>
    [HttpGet("parts")]
    public async Task<IActionResult> Parts([FromQuery] string? search, [FromQuery] int? warehouseId, [FromQuery] bool? counted, [FromQuery] bool? mine, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var r = await svc.PartsAsync(UserId, new PageQuery(page, pageSize, search), warehouseId, counted, mine == true);
        return r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : Ok(ApiResponse.Ok(PageDto<MobilePart>.From(r.Data!)));
    }

    /// <summary>تفاصيل قطعة.</summary>
    [HttpGet("parts/{id:int}")]
    public async Task<IActionResult> Part(int id) => From(await svc.PartAsync(UserId, id));

    /// <summary>تسجيل عدّ قطعة (بالمعرّف، أو برقم القطعة والمستودع لقطعة غير مدرجة) مع الموقع والـ GPS.</summary>
    [HttpPost("counts")]
    public async Task<IActionResult> Count([FromBody] CountInput input) => From(await svc.CountAsync(UserId, input), "تم حفظ العد");

    /// <summary>مسح VIN مركبة مع GPS.</summary>
    [HttpPost("vins/scan")]
    public async Task<IActionResult> Scan([FromBody] ScanInput input) => From(await svc.ScanVinAsync(UserId, input), "تم تسجيل المركبة");

    /// <summary>المركبات اللي مسحها المستخدم بالجلسة الحالية.</summary>
    [HttpGet("vins")]
    public async Task<IActionResult> MyVins([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse.Ok(PageDto<MobileVin>.From(await svc.MyVinsAsync(UserId, new PageQuery(page, pageSize, search)))));

    /// <summary>إرسال طلب تصحيح VIN لمركبة مسحها المستخدم.</summary>
    [HttpPost("vin-corrections")]
    public async Task<IActionResult> VinCorrection([FromBody] VinCorrectionInput input) => From(await svc.VinCorrectionAsync(UserId, input), "تم إرسال طلب التصحيح");

    /// <summary>طلبات تصحيح VIN الخاصة بالمستخدم.</summary>
    [HttpGet("vin-corrections")]
    public async Task<IActionResult> MyVinCorrections([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse.Ok(PageDto<MobileVinRequest>.From(await svc.MyVinCorrectionsAsync(UserId, new PageQuery(page, pageSize)))));

    /// <summary>مهام إعادة العد المسندة للمستخدم (قيد الانتظار).</summary>
    [HttpGet("recounts")]
    public async Task<IActionResult> Recounts([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse.Ok(PageDto<MobileRecount>.From(await svc.RecountsAsync(UserId, new PageQuery(page, pageSize)))));

    /// <summary>إرسال نتيجة إعادة العد.</summary>
    [HttpPost("recounts/{id:int}/submit")]
    public async Task<IActionResult> SubmitRecount(int id, [FromBody] RecountInput input) => From(await svc.SubmitRecountAsync(UserId, id, input), "تم حفظ إعادة العد");

    /// <summary>إرسال طلب تعديل كمية قطعة (يروح للإدارة للموافقة).</summary>
    [HttpPost("qty-requests")]
    public async Task<IActionResult> QtyRequest([FromBody] QtyRequestInput input) => From(await svc.QtyRequestAsync(UserId, input), "تم إرسال الطلب");

    /// <summary>طلبات تعديل الكمية الخاصة بالمستخدم.</summary>
    [HttpGet("qty-requests")]
    public async Task<IActionResult> MyQtyRequests([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse.Ok(PageDto<MobileQtyRequest>.From(await svc.MyQtyRequestsAsync(UserId, new PageQuery(page, pageSize)))));

    /// <summary>تغيير كلمة مرور المستخدم.</summary>
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordInput input)
    {
        var r = await svc.ChangePasswordAsync(UserId, input);
        return r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : Ok(ApiResponse.Ok(null, "تم تغيير كلمة المرور"));
    }

    /// <summary>سجل المستخدم بالجلسة الحالية (type = part | vin | فارغ للكل).</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string? type, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (type is not (null or "" or "part" or "vin")) return BadRequest(ApiResponse.Fail("type لازم يكون part أو vin"));
        return Ok(ApiResponse.Ok(PageDto<HistoryItem>.From(await svc.HistoryAsync(UserId, new PageQuery(page, pageSize, search), string.IsNullOrEmpty(type) ? null : type))));
    }
}
