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
public sealed class MobileApiController(MobileService svc, NewPartRequestService newParts, TokenService tokens) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirst("sub")!.Value);

    private IActionResult From<T>(MobileResult<T> r, string? okMessage = null) =>
        r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error, data: r.Detail)) : StatusCode(r.Status, ApiResponse.Ok(r.Data, r.Message ?? okMessage));

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

    /// <summary>الدخول كضيف بدون رقم وكلمة مرور. الضيف يشوف قطع وسيارات كل المخازن بالجلسة المفتوحة، وكل جرد أو مسح أو طلب يرجع 403.</summary>
    [HttpPost("auth/guest")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    [ProducesResponseType(typeof(ApiResponse), 200)]
    [ProducesResponseType(typeof(ApiResponse), 403)]
    public async Task<IActionResult> GuestLogin()
    {
        var u = await svc.GuestLoginAsync();
        if (u == null) return StatusCode(403, ApiResponse.Fail("الدخول كضيف متوقف حالياً"));
        var (token, exp) = tokens.Create(u);
        return Ok(ApiResponse.Ok(new TokenDto(token, "Bearer", exp, await svc.ProfileAsync(u.Id) ?? (object)new { })));
    }

    /// <summary>بيانات المستخدم الحالي ومستودعاته ونوع الجرد المسموح (countType: parts | vehicles | both).</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(ApiResponse.Ok(await svc.ProfileAsync(UserId)));

    /// <summary>جلسة الجرد الحالية (قيد التنفيذ)، أو null.</summary>
    [HttpGet("sessions/current")]
    public async Task<IActionResult> CurrentSession()
    {
        var s = await svc.CurrentSessionAsync();
        return Ok(ApiResponse.Ok(s, s == null ? "لا توجد جلسة جرد قيد التنفيذ" : null));
    }

    /// <summary>قطع الغيار في الجلسة الحالية ضمن مستودعات المستخدم، مع بحث وترقيم. code = مطابقة رقم القطعة بعد التطبيع (بدون مسافات/شرطات).</summary>
    [HttpGet("parts")]
    public async Task<IActionResult> Parts([FromQuery] string? search, [FromQuery] int? warehouseId, [FromQuery] bool? counted, [FromQuery] bool? mine, [FromQuery] string? code, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var r = await svc.PartsAsync(UserId, new PageQuery(page, pageSize, search), warehouseId, counted, mine == true, code);
        return r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : Ok(ApiResponse.Ok(PageDto<MobilePart>.From(r.Data!)));
    }

    /// <summary>تفاصيل قطعة.</summary>
    [HttpGet("parts/{id:int}")]
    public async Task<IActionResult> Part(int id) => From(await svc.PartAsync(UserId, id));

    /// <summary>تسجيل عدّ قطعة مع الموقع والـ GPS. القطعة المعدودة يعيد عدّها نفس الموظف فقط؛ غيره يستلم 409 باسم من عدّها. الزيادة والنقص ينحفظان مباشرة ويظهران بالمالية.</summary>
    [HttpPost("counts")]
    public async Task<IActionResult> Count([FromBody] CountInput input) => From(await svc.CountAsync(UserId, input), "تم حفظ العد");

    /// <summary>مسح VIN مركبة مع GPS.</summary>
    [HttpPost("vins/scan")]
    public async Task<IActionResult> Scan([FromBody] ScanInput input) => From(await svc.ScanVinAsync(UserId, input), "تم تسجيل المركبة");

    /// <summary>نوع المركبة لرقم VIN (من ملف الجلسة) قبل تسجيلها.</summary>
    [HttpGet("vins/lookup")]
    public async Task<IActionResult> LookupVin([FromQuery] string? vin) => From(await svc.LookupVinAsync(UserId, vin));

    /// <summary>المركبات التي مسحها المستخدم في الجلسة الحالية.</summary>
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
    public async Task<IActionResult> Recounts([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] bool includeCompleted = false)
        => Ok(ApiResponse.Ok(PageDto<MobileRecount>.From(await svc.RecountsAsync(UserId, new PageQuery(page, pageSize), includeCompleted))));

    /// <summary>إرسال نتيجة إعادة العد.</summary>
    [HttpPost("recounts/{id:int}/submit")]
    public async Task<IActionResult> SubmitRecount(int id, [FromBody] RecountInput input) => From(await svc.SubmitRecountAsync(UserId, id, input), "تم حفظ إعادة العد");

    /// <summary>طلب إعادة جرد قطعة معدودة (لمن جردها أول مرة فقط، ويحتاج موافقة الإدارة).</summary>
    [HttpPost("parts/{id:int}/recount-ask")]
    public async Task<IActionResult> AskRecount(int id) => From(await svc.AskRecountAsync(UserId, id), "تم إرسال الطلب");

    /// <summary>إرسال طلب إضافة قطعة غير موجودة في جلسة الجرد. لا يُنشئ قطعة قبل الموافقة النهائية.</summary>
    [HttpPost("new-part-requests")]
    public async Task<IActionResult> CreateNewPart([FromBody] NewPartInput input) => From(await newParts.CreateAsync(UserId, input), "تم إرسال طلب الإضافة");

    [HttpPut("new-part-requests/{id:int}")]
    public async Task<IActionResult> ResubmitNewPart(int id, [FromBody] NewPartInput input) => From(await newParts.ResubmitAsync(UserId, id, input), "تم تعديل الطلب وإعادة إرساله");

    /// <summary>طلبات إضافة القطعة الخاصة بالمستخدم الحالي فقط.</summary>
    [HttpGet("new-part-requests")]
    public async Task<IActionResult> MyNewParts([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        => Ok(ApiResponse.Ok(PageDto<MobileNewPart>.From(await newParts.MineAsync(UserId, new PageQuery(page, pageSize)))));

    /// <summary>إلغاء طلب أُرجع إلى الموظف. الموظف صاحب الطلب فقط، والحالة رجع للموظف فقط.</summary>
    [HttpPost("new-part-requests/{id:int}/cancel")]
    public async Task<IActionResult> CancelNewPart(int id) => From(await newParts.CancelAsync(UserId, id), "تم إلغاء الطلب بنجاح");

    public sealed class DeviceInput { public string? Token { get; set; } public string? Platform { get; set; } }

    /// <summary>يربط رمز الإشعارات بالمستخدم المسجّل. لا يُقبل رقم مستخدم من الطلب.</summary>
    [HttpPost("devices")]
    public async Task<IActionResult> RegisterDevice([FromBody] DeviceInput input)
        => From(await svc.RegisterDeviceAsync(UserId, input.Token, input.Platform), "تم تسجيل الجهاز");

    /// <summary>يعطّل رمز هذا المستخدم عند الخروج. رمز مستخدم آخر لا يُنقل ولا يُعطّل.</summary>
    [HttpPost("devices/deactivate")]
    public async Task<IActionResult> DeactivateDevice([FromBody] DeviceInput input)
        => From(await svc.DeactivateDeviceAsync(UserId, input.Token), "تم إيقاف الجهاز");

    /// <summary>تغيير كلمة مرور المستخدم.</summary>
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordInput input)
    {
        var r = await svc.ChangePasswordAsync(UserId, input);
        return r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : Ok(ApiResponse.Ok(null, "تم تغيير كلمة المرور"));
    }

    public sealed class DeleteAccountInput { public string? Password { get; set; } }

    /// <summary>حذف حساب المستخدم نفسه بعد تأكيد كلمة المرور. الحساب يتعطل فوراً، ورقم الهاتف وكلمة المرور ينمسحون، وسجل الجرد يبقى.</summary>
    [HttpPost("me/delete")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountInput input)
    {
        var r = await svc.DeleteAccountAsync(UserId, input.Password);
        return r.Error != null ? StatusCode(r.Status, ApiResponse.Fail(r.Error)) : Ok(ApiResponse.Ok(null, "تم حذف الحساب"));
    }

    /// <summary>سجل المستخدم في الجلسة الحالية (type = part | vin | فارغ للكل).</summary>
    [HttpGet("history")]
    public async Task<IActionResult> History([FromQuery] string? type, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (type is not (null or "" or "part" or "vin")) return BadRequest(ApiResponse.Fail("يجب أن تكون قيمة type إما part أو vin"));
        return Ok(ApiResponse.Ok(PageDto<HistoryItem>.From(await svc.HistoryAsync(UserId, new PageQuery(page, pageSize, search), string.IsNullOrEmpty(type) ? null : type))));
    }
}
