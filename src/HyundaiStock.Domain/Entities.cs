namespace HyundaiStock.Domain;

public enum AdminRole { MainAdmin = 0, SuperAdmin = 1, Admin = 2 }
public enum WarehouseType { Type1 = 1, Type2 = 2 }
public enum SessionStatus { InProgress = 0, Completed = 1 }
public enum RequestStatus { Pending = 0, Approved = 1, Rejected = 2 }
public enum ApprovalLevel { None = 0, First = 1, Final = 2 }
public enum RecountStatus { Pending = 0, Completed = 1 }

public abstract class Entity
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class Branch : Entity
{
    public string Name { get; set; } = "";
    public ICollection<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
}

public class Warehouse : Entity
{
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
}

/// <summary>Mobile app user (counter).</summary>
public class AppUser : Entity
{
    public string Name { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
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

public class StocktakingSession : Entity
{
    public int Year { get; set; }
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
    public string Vin { get; set; } = "";
    public string? Model { get; set; }
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
    public int RequestedByAdminId { get; set; }
    public Admin? RequestedByAdmin { get; set; }
    public ApprovalLevel ApprovalLevel { get; set; } = ApprovalLevel.None;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public int? FirstApprovedByAdminId { get; set; }
    public int? FinalApprovedByAdminId { get; set; }
    public int? RejectedByAdminId { get; set; }
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
    public static string Role(AdminRole r) => r switch { AdminRole.MainAdmin => "مسؤول رئيسي", AdminRole.SuperAdmin => "سوبر أدمن", _ => "أدمن" };
    public static string Type(WarehouseType t) => t == WarehouseType.Type1 ? "نوع 1" : "نوع 2";
    public static string Session(SessionStatus s) => s == SessionStatus.InProgress ? "قيد التنفيذ" : "مكتمل";
    public static string Qty(RequestStatus s) => s switch { RequestStatus.Pending => "قيد الانتظار", RequestStatus.Approved => "مقبول", _ => "مرفوض" };
    public static string Vin(RequestStatus s) => s switch { RequestStatus.Pending => "قيد الانتظار", RequestStatus.Approved => "موافق عليه", _ => "مرفوض" };
    public static string Level(ApprovalLevel l) => l switch { ApprovalLevel.None => "القبول الأول", ApprovalLevel.First => "القبول النهائي", _ => "مكتمل" };
    public static string Recount(RecountStatus s) => s == RecountStatus.Pending ? "قيد الانتظار" : "مكتمل";
}
