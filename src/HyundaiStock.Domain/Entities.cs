namespace HyundaiStock.Domain;

public enum AdminRole { MainAdmin = 0, SuperAdmin = 1, Admin = 2, WarehouseManager = 3, SparePartsManager = 4, Accountant = 5, AccountsManager = 6, VehicleWarehouseManager = 7 }
public enum WarehouseType { Type1 = 1, Type2 = 2 }
public enum SessionStatus { InProgress = 0, Completed = 1 }
public enum RequestStatus { Pending = 0, Approved = 1, Rejected = 2 }
public enum ApprovalLevel { None = 0, First = 1, Final = 2 }
public enum RecountStatus { Pending = 0, Completed = 1 }
/// <summary>What a mobile user may count. Existing users get Both.</summary>
public enum CountType { Parts = 1, Vehicles = 2, Both = 3 }

/// <summary>Missing-part request. Terminal states are Approved, Cancelled and SessionClosed.</summary>
public enum NewPartRequestStatus
{
    PendingSparePartsManager = 0,
    PendingAccountant = 1,
    ReturnedToEmployee = 2,
    Approved = 3,
    Cancelled = 4,
    SessionClosed = 5
}

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Branch : Entity
{
    /// <summary>BranchNo. used by the Hyundai system Excel files.</summary>
    public int? Number { get; set; }
    public string Name { get; set; } = "";
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}

public class Warehouse : Entity
{
    /// <summary>WarehouseNo. used by the Hyundai system Excel files.</summary>
    public int? Number { get; set; }
    public string Name { get; set; } = "";
    public WarehouseType Type { get; set; } = WarehouseType.Type1;
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}

/// <summary>Dashboard account (مسؤول رئيسي / سوبر أدمن / أدمن).</summary>
public class Admin : Entity
{
    public string Name { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public AdminRole Role { get; set; } = AdminRole.Admin;
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Explicit grant to see every branch. Null BranchId never means this.</summary>
    public bool AllBranches { get; set; }
    public ICollection<AdminBranch> Branches { get; set; } = new List<AdminBranch>();
    public ICollection<AdminWarehouse> Warehouses { get; set; } = new List<AdminWarehouse>();
}

/// <summary>Branches an accountant may access. Cleared when the role changes.</summary>
public class AdminBranch
{
    public int AdminId { get; set; }
    public Admin? Admin { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}

/// <summary>Warehouses a warehouse manager may access. Cleared when the role changes.</summary>
public class AdminWarehouse
{
    public int AdminId { get; set; }
    public Admin? Admin { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
}

/// <summary>Mobile app user (counter).</summary>
public class AppUser : Entity
{
    public string Name { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    /// <summary>When one part number has several rows, this user sees every row and picks one. Without it the default row is counted.</summary>
    public bool CanPickDuplicateRow { get; set; }
    /// <summary>Allowed count type: spare parts, vehicles (VIN scans), or both. Enforced by the API.</summary>
    public CountType CountType { get; set; } = CountType.Both;
    /// <summary>Guest (view only): opens the app and sees its warehouses, but every count, scan and request is refused with 403.</summary>
    public bool IsGuest { get; set; }
    /// <summary>Set when the user deletes the account from the app. The row stays for count history; login data is erased and the account is hidden from the dashboard.</summary>
    public DateTime? DeletedAt { get; set; }
    public int? CreatedByAdminId { get; set; }
    public Admin? CreatedByAdmin { get; set; }
    public ICollection<AppUserBranch> Branches { get; set; } = new List<AppUserBranch>();
    public ICollection<AppUserWarehouse> Warehouses { get; set; } = new List<AppUserWarehouse>();
}

public class AppUserBranch
{
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
}

public class AppUserWarehouse
{
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
}

/// <summary>Annual count (one per year) or monthly count (one per month). Both work the same way: parts, vehicles and variances to accounts.</summary>
public enum SessionKind { Annual = 0, Monthly = 1 }

public class StocktakingSession : Entity
{
    public int Year { get; set; }
    public SessionKind Kind { get; set; } = SessionKind.Annual;
    /// <summary>1 to 12 for a monthly count, null for an annual one.</summary>
    public int? Month { get; set; }
    /// <summary>Shown everywhere a session is named: «جرد سنوي 2026» or «جرد شباط 2026». Set when the session starts.</summary>
    public string Name { get; set; } = "";
    public SessionStatus Status { get; set; } = SessionStatus.InProgress;
    public int StartedByAdminId { get; set; }
    public Admin? StartedByAdmin { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public int? EndedByAdminId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public ICollection<InventoryPart> Parts { get; set; } = new List<InventoryPart>();
    public ICollection<ExpectedVin> ExpectedVins { get; set; } = new List<ExpectedVin>();
}

/// <summary>A spare part line loaded from the session Excel (system stock) plus its count.</summary>
public class InventoryPart : Entity
{
    public int SessionId { get; set; }
    public StocktakingSession? Session { get; set; }
    public string PartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Price { get; set; }
    public int SystemQuantity { get; set; }
    public string? SystemLocation { get; set; }
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int? CountedQuantity { get; set; }
    public string? CountedLocation { get; set; }
    public int? CountedByUserId { get; set; }
    public AppUser? CountedByUser { get; set; }
    public DateTime? CountedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public ICollection<PartChangeLog> ChangeLogs { get; set; } = new List<PartChangeLog>();

    public int? Difference => CountedQuantity.HasValue ? CountedQuantity.Value - SystemQuantity : null;
    public bool IsMatched => CountedQuantity.HasValue && CountedQuantity.Value == SystemQuantity
        && (string.IsNullOrWhiteSpace(SystemLocation) || string.Equals(SystemLocation?.Trim(), CountedLocation?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public class PartChangeLog : Entity
{
    public int InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedBy { get; set; } = "";
    public string Source { get; set; } = "";
}

public class ExpectedVin : Entity
{
    public int SessionId { get; set; }
    public StocktakingSession? Session { get; set; }
    /// <summary>Chassis number (رقم الشاصي). Stored as the 17-character VIN.</summary>
    public string Vin { get; set; } = "";
    public string? Model { get; set; }
    /// <summary>Agent (الوكيل) from the session file. Optional.</summary>
    public string? Dealer { get; set; }
    /// <summary>Manufacture year (سنة الصنع) from the session file. Optional.</summary>
    public int? ManufactureYear { get; set; }
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int? BranchId { get; set; }
    public Branch? Branch { get; set; }
}

/// <summary>A vehicle scanned in the app (المركبات).</summary>
public class ScannedVin : Entity
{
    public int SessionId { get; set; }
    public StocktakingSession? Session { get; set; }
    public string Vin { get; set; } = "";
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

/// <summary>Retired: quantity-correction requests were removed from the dashboard and the app. The table stays for history.</summary>
public class QuantityCorrectionRequest : Entity
{
    public int? InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public string PartNumber { get; set; } = "";
    public string PartDescription { get; set; } = "";
    public int? OldQuantity { get; set; }
    public int NewQuantity { get; set; }
    public string Reason { get; set; } = "";
    public int? BranchId { get; set; }
    public int? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    /// <summary>1 = one user, 2 = users of the branch, 3 = users of the warehouse. Null on older and mobile-created rows.</summary>
    public int? AudienceKind { get; set; }
    public int? AudienceUserId { get; set; }
    public AppUser? AudienceUser { get; set; }
    /// <summary>Set when an admin created the request from the dashboard.</summary>
    public int? RequestedByAdminId { get; set; }
    public Admin? RequestedByAdmin { get; set; }
    /// <summary>Set when a counter created the request from the mobile app.</summary>
    public int? RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public ApprovalLevel ApprovalLevel { get; set; } = ApprovalLevel.None;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int? FirstApprovedByAdminId { get; set; }
    public DateTime? FirstApprovedAt { get; set; }
    public int? FinalApprovedByAdminId { get; set; }
    public DateTime? FinalApprovedAt { get; set; }
    public int? RejectedByAdminId { get; set; }
    public DateTime? RejectedAt { get; set; }
    /// <summary>Latest rejection reason. Earlier reasons stay in <see cref="WorkflowAudit"/>.</summary>
    public string? DecisionReason { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RecountRequest : Entity
{
    public int InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public int AssignedUserId { get; set; }
    public AppUser? AssignedUser { get; set; }
    public int RequestedByAdminId { get; set; }
    public Admin? RequestedByAdmin { get; set; }
    public RecountStatus Status { get; set; } = RecountStatus.Pending;
    public int? ResultQuantity { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public enum RecountAskStatus { Pending = 0, Approved = 1, Rejected = 2 }

/// <summary>The counter who counted a part asks to count it again. Approval creates a recount task for that same counter and notifies them.</summary>
public class RecountAsk : Entity
{
    public int InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public int RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public RecountAskStatus Status { get; set; } = RecountAskStatus.Pending;
    public int? DecidedByAdminId { get; set; }
    public Admin? DecidedByAdmin { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? DecisionReason { get; set; }
}

/// <summary>A counter asked to add a part that is not in the current session. Inventory is created only on final approval.</summary>
public class NewPartRequest : Entity
{
    public string PartNumber { get; set; } = "";
    public string NormalizedPartNumber { get; set; } = "";
    public string Description { get; set; } = "";
    public int Quantity { get; set; }
    public string Location { get; set; } = "";
    public int WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public int BranchId { get; set; }
    public Branch? Branch { get; set; }
    public int SessionId { get; set; }
    public StocktakingSession? Session { get; set; }
    public int RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public NewPartRequestStatus Status { get; set; } = NewPartRequestStatus.PendingSparePartsManager;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? SparePartsApprovedByAdminId { get; set; }
    public DateTime? SparePartsApprovedAt { get; set; }
    public int? AccountantApprovedByAdminId { get; set; }
    public DateTime? AccountantApprovedAt { get; set; }
    public string? ReturnReason { get; set; }
    public DateTime? CancelledAt { get; set; }
    public int? InventoryPartId { get; set; }
    public InventoryPart? InventoryPart { get; set; }
    public ICollection<WorkflowAudit> Audits { get; set; } = new List<WorkflowAudit>();
}

/// <summary>Append-only approval history for quantity and missing-part requests.</summary>
public class WorkflowAudit : Entity
{
    public string Subject { get; set; } = "";
    public int SubjectId { get; set; }
    public string Action { get; set; } = "";
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public int? ActorAdminId { get; set; }
    public Admin? ActorAdmin { get; set; }
    public int? ActorUserId { get; set; }
    public AppUser? ActorUser { get; set; }
    public string ActorRole { get; set; } = "";
    public string? Reason { get; set; }
    public int? SessionId { get; set; }
    public int? WarehouseId { get; set; }
    public int? BranchId { get; set; }
    public int? InventoryPartId { get; set; }
    public int? NewPartRequestId { get; set; }
    public NewPartRequest? NewPartRequest { get; set; }
}

public static class AuditActions
{
    public const string Quantity = "Quantity";
    public const string NewPart = "NewPart";
    public const string Submitted = "Submitted";
    public const string FirstApproved = "FirstApproved";
    public const string Returned = "Returned";
    public const string FinalApproved = "FinalApproved";
    public const string Rejected = "Rejected";
    public const string Cancelled = "Cancelled";
    public const string SessionClosed = "SessionClosed";
}

/// <summary>Push token owned by the authenticated mobile user. A token belongs to one user at a time.</summary>
public class DeviceRegistration : Entity
{
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public string Token { get; set; } = "";
    public string Platform { get; set; } = "android";
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class VinCorrectionRequest : Entity
{
    public int ScannedVinId { get; set; }
    public ScannedVin? ScannedVin { get; set; }
    public string CurrentVin { get; set; } = "";
    public string NewVin { get; set; } = "";
    public string Reason { get; set; } = "";
    public int RequestedByUserId { get; set; }
    public AppUser? RequestedByUser { get; set; }
    public int? ApprovedByAdminId { get; set; }
    public Admin? ApprovedByAdmin { get; set; }
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime? ApprovedAt { get; set; }
}

public static class Labels
{
    public static string Role(AdminRole r) => r switch
    {
        AdminRole.MainAdmin => "مسؤول رئيسي",
        AdminRole.SuperAdmin => "سوبر أدمن",
        AdminRole.WarehouseManager => "مدير مخزن",
        AdminRole.SparePartsManager => "مدير قطع الغيار",
        AdminRole.Accountant => "محاسب",
        AdminRole.AccountsManager => "مدير حسابات",
        AdminRole.VehicleWarehouseManager => "مدير مخزن سيارات",
        _ => "أدمن"
    };
    public static string RoleHint(AdminRole r) => r switch
    {
        AdminRole.MainAdmin => "يشوف كل شيء في النظام: المشرفين، الجلسات، الجرد، الطلبات، والمالية.",
        AdminRole.SuperAdmin => "يدير الجلسات والفروع والمخازن، ويشوف كل البيانات بدون إنشاء مسؤول رئيسي.",
        AdminRole.Admin => "يدير مستخدمي فرعه ويوافق على تصحيح VIN وإعادة العد.",
        AdminRole.WarehouseManager => "يشوف مخازنه فقط ويطلب إعادة العد.",
        AdminRole.SparePartsManager => "اطلاع فقط على تقدّم جرد القطع والفروقات (أعلى / أقل / مطابق) بدون المركبات أو كمية النظام أو السعر أو التعديل.",
        AdminRole.Accountant => "المالية (الفروقات بالزيادة والنقص)، ويطلب إعادة العد ضمن الفروع المحددة.",
        AdminRole.AccountsManager => "نفس صلاحيات المحاسب: المالية وطلب إعادة العد، ويبدأ جلسة جرد.",
        AdminRole.VehicleWarehouseManager => "المركبات وتصحيح VIN واطلاع على تقدّم الجرد بدون تعديل الكميات أو المالية.",
        _ => ""
    };
    /// <summary>Higher / lower / matching. Does not reveal the system quantity.</summary>
    public static string Direction(int? counted, int system) => counted is not int c ? "—" : c > system ? "أعلى" : c < system ? "أقل" : "مطابق";
    public static string Type(WarehouseType t) => t == WarehouseType.Type1 ? "نوع 1" : "نوع 2";
    public static string Session(SessionStatus s) => s == SessionStatus.InProgress ? "قيد التنفيذ" : "مكتمل";
    public static readonly string[] Months = { "كانون الثاني", "شباط", "آذار", "نيسان", "أيار", "حزيران", "تموز", "آب", "أيلول", "تشرين الأول", "تشرين الثاني", "كانون الأول" };
    public static string MonthName(int month) => month is >= 1 and <= 12 ? Months[month - 1] : month.ToString();
    public static string SessionKindLabel(SessionKind k) => k == SessionKind.Monthly ? "شهري" : "سنوي";
    /// <summary>Display name of a session: «جرد سنوي 2026» or «جرد شباط 2026».</summary>
    public static string SessionName(SessionKind kind, int year, int? month) =>
        kind == SessionKind.Monthly && month is int m ? $"جرد {MonthName(m)} {year}" : $"جرد سنوي {year}";
    /// <summary>ASCII code for file names: 2026 or 2026-02.</summary>
    public static string SessionCode(SessionKind kind, int year, int? month) =>
        kind == SessionKind.Monthly && month is int m ? $"{year}-{m:00}" : year.ToString();
    public static string Qty(RequestStatus s) => s switch { RequestStatus.Pending => "قيد الانتظار", RequestStatus.Approved => "مقبول", _ => "مرفوض" };
    /// <summary>Labels shown to the counter. Pending is split by the existing approval level so older rows stay valid.</summary>
    public static string QtyMobile(RequestStatus s, ApprovalLevel l) => s switch
    {
        RequestStatus.Approved => "مقبول",
        RequestStatus.Rejected => "مرفوض",
        _ => l == ApprovalLevel.First ? "بانتظار المالية" : "بانتظار مدير المخزن"
    };
    public static string NewPart(NewPartRequestStatus s) => s switch
    {
        NewPartRequestStatus.PendingSparePartsManager => "بانتظار مدير قطع الغيار",
        NewPartRequestStatus.PendingAccountant => "بانتظار الحسابات",
        NewPartRequestStatus.ReturnedToEmployee => "رجع للموظف",
        NewPartRequestStatus.Approved => "مثبت",
        NewPartRequestStatus.Cancelled => "ملغي",
        _ => "انتهت جلسة الجرد"
    };
    public static bool NewPartOpen(NewPartRequestStatus s) => s is NewPartRequestStatus.PendingSparePartsManager or NewPartRequestStatus.PendingAccountant or NewPartRequestStatus.ReturnedToEmployee;
    public static string Vin(RequestStatus s) => s switch { RequestStatus.Pending => "قيد الانتظار", RequestStatus.Approved => "موافق عليه", _ => "مرفوض" };
    public static string Level(ApprovalLevel l) => l switch { ApprovalLevel.None => "القبول الأول", ApprovalLevel.First => "القبول النهائي", _ => "مكتمل" };
    public static string Recount(RecountStatus s) => s == RecountStatus.Pending ? "قيد المعالجة" : "مكتمل";
    public static string CountTypeLabel(CountType t) => t switch { CountType.Parts => "قطع الغيار فقط", CountType.Vehicles => "السيارات فقط", _ => "قطع الغيار والسيارات" };
    /// <summary>Count type shown for a mobile user; a guest counts nothing.</summary>
    public static string UserCountLabel(CountType t, bool guest) => guest ? "ضيف (مشاهدة فقط)" : CountTypeLabel(t);
    /// <summary>API code for the count type: parts | vehicles | both.</summary>
    public static string CountTypeCode(CountType t) => t switch { CountType.Parts => "parts", CountType.Vehicles => "vehicles", _ => "both" };
    /// <summary>Finance wording for a count against the system quantity.</summary>
    public static string Variance(int? counted, int system) => counted is not int c ? "غير معدود" : c > system ? "زيادة" : c < system ? "نقص" : "مطابق";
}
