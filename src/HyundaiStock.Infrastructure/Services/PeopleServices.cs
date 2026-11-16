using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record AdminRow(int Id, string Name, string PhoneNumber, AdminRole Role, int? BranchId, string? BranchName, bool IsActive, DateTime CreatedAt, bool AllBranches);
public sealed record UserRow(int Id, string Name, string PhoneNumber, List<string> Branches, List<string> Warehouses, bool IsActive, DateTime CreatedAt);
public sealed record UserDetails(int Id, string Name, string PhoneNumber, List<int> BranchIds, List<int> WarehouseIds, List<string> Branches, List<string> Warehouses, bool IsActive, DateTime CreatedAt, bool CanPickDuplicateRow = false);

public sealed class AdminInput
{
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
    public int? BranchId { get; set; }
    public AdminRole Role { get; set; } = AdminRole.Admin;
    public bool AllBranches { get; set; }
    public List<int> BranchIds { get; set; } = new();
    public List<int> WarehouseIds { get; set; } = new();
}

public sealed class UserInput
{
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public List<int> BranchIds { get; set; } = new();
    public List<int> WarehouseIds { get; set; } = new();
    /// <summary>Lets the counter pick the row when a part number repeats.</summary>
    public bool CanPickDuplicateRow { get; set; }
}

public sealed class AdminService(AppDbContext db, IPasswordService pw)
{
    public Task<PagedResult<AdminRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = db.Admins.AsNoTracking().AsQueryable();
        if (!s.IsMain) q = q.Where(x => x.Role != AdminRole.MainAdmin);
        if (p.Term is string t) q = q.Where(x => x.PhoneNumber.Contains(t) || x.Name.Contains(t));
        var rows = q.OrderBy(x => x.Id).Select(x => new AdminRow(x.Id, x.Name, x.PhoneNumber, x.Role, x.BranchId, x.Branch != null ? x.Branch.Name : null, x.IsActive, x.CreatedAt, x.AllBranches));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public Task<AdminRow?> GetAsync(int id) => db.Admins.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new AdminRow(x.Id, x.Name, x.PhoneNumber, x.Role, x.BranchId, x.Branch != null ? x.Branch.Name : null, x.IsActive, x.CreatedAt, x.AllBranches)).FirstOrDefaultAsync();


    public async Task<Dictionary<string, string>> CreateAsync(AdminInput i, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        var name = i.Name?.Trim(); var phone = AccountPhones.Normalize(i.PhoneNumber);
        if (string.IsNullOrEmpty(name) || name.Length < 2) err["Name"] = "اسم المشرف مطلوب (حرفين على الأقل)";
        if (!Validators.IsIraqiPhone(phone)) err["PhoneNumber"] = Validators.PhoneError;
        else if (await AccountPhones.IsUsedAsync(db, phone)) err["PhoneNumber"] = AccountPhones.DuplicateError;
        if (string.IsNullOrEmpty(i.Password) || i.Password.Length < 6) err["Password"] = "كلمة المرور 6 أحرف على الأقل";
        var role = s.IsMain ? i.Role : AdminRole.Admin;
        if (!Enum.IsDefined(role)) err["Role"] = "الدور غير صالح";
        if (!s.IsSuper) i.AllBranches = false;
        foreach (var kv in await ValidateScope(role, i)) err[kv.Key] = kv.Value;
        if (err.Count > 0) return err;
        var admin = new Admin { Name = name!, PhoneNumber = phone!, PasswordHash = pw.Hash(i.Password!), Role = role, IsActive = true };
        db.Admins.Add(admin);
        ApplyScope(admin, role, i);
        await db.SaveChangesAsync();
        return err;
    }

    public async Task<Dictionary<string, string>> UpdateAsync(int id, AdminInput i, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        var (a, e) = await Target(id, s);
        if (a == null) { err[""] = e!; return err; }
        var name = i.Name?.Trim();
        var phone = AccountPhones.Normalize(i.PhoneNumber);
        if (!Validators.IsIraqiPhone(phone)) err["PhoneNumber"] = Validators.PhoneError;
        else if (await AccountPhones.IsUsedAsync(db, phone, adminId: id)) err["PhoneNumber"] = AccountPhones.DuplicateError;
        if (string.IsNullOrEmpty(name) || name.Length < 2) err["Name"] = "اسم المشرف مطلوب (حرفين على الأقل)";
        var role = s.IsMain ? i.Role : a.Role;
        if (!Enum.IsDefined(role)) err["Role"] = "الدور غير صالح";
        if (!s.IsSuper) i.AllBranches = false;
        foreach (var kv in await ValidateScope(role, i)) err[kv.Key] = kv.Value;
        if (err.Count > 0) return err;
        a.Name = name!; a.PhoneNumber = phone; a.Role = role;
        db.AdminBranches.RemoveRange(await db.AdminBranches.Where(x => x.AdminId == a.Id).ToListAsync());
        db.AdminWarehouses.RemoveRange(await db.AdminWarehouses.Where(x => x.AdminId == a.Id).ToListAsync());
        ApplyScope(a, role, i);
        await db.SaveChangesAsync();
        return err;
    }

    public async Task<(List<int> Branches, List<int> Warehouses)> AssignmentsAsync(int id) =>
        (await db.AdminBranches.Where(x => x.AdminId == id).Select(x => x.BranchId).ToListAsync(),
         await db.AdminWarehouses.Where(x => x.AdminId == id).Select(x => x.WarehouseId).ToListAsync());

    private async Task<Dictionary<string, string>> ValidateScope(AdminRole role, AdminInput i)
    {
        var err = new Dictionary<string, string>();
        var wide = i.AllBranches && role is AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.Accountant or AdminRole.AccountsManager or AdminRole.VehicleWarehouseManager;
        if (wide) return err;
        if (role is AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager)
        { if (i.BranchId is not int bid || !await db.Branches.AnyAsync(x => x.Id == bid)) err["BranchId"] = "اختر فرعًا واحدًا"; }
        else if (role is AdminRole.Accountant or AdminRole.AccountsManager)
        { var ids = (i.BranchIds ?? new()).Distinct().ToList(); if (ids.Count == 0 || await db.Branches.CountAsync(x => ids.Contains(x.Id)) != ids.Count) err["BranchIds"] = "اختر فرعًا واحدًا على الأقل"; }
        else if (role == AdminRole.WarehouseManager)
        { var ids = (i.WarehouseIds ?? new()).Distinct().ToList(); if (ids.Count == 0 || await db.Warehouses.CountAsync(x => ids.Contains(x.Id)) != ids.Count) err["WarehouseIds"] = "اختر مخزنًا واحدًا على الأقل"; }
        return err;
    }

    private void ApplyScope(Admin a, AdminRole role, AdminInput i)
    {
        var wide = i.AllBranches && role is AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.Accountant or AdminRole.AccountsManager or AdminRole.VehicleWarehouseManager;
        a.AllBranches = wide;
        a.BranchId = !wide && role is AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager ? i.BranchId : null;
        if (!wide && role is AdminRole.Accountant or AdminRole.AccountsManager) foreach (var id in (i.BranchIds ?? new()).Distinct()) db.AdminBranches.Add(new AdminBranch { Admin = a, BranchId = id });
        if (role == AdminRole.WarehouseManager) foreach (var id in (i.WarehouseIds ?? new()).Distinct()) db.AdminWarehouses.Add(new AdminWarehouse { Admin = a, WarehouseId = id });
    }

    private async Task<(Admin? a, string? e)> Target(int id, AdminScope s)
    {
        var a = await db.Admins.FindAsync(id);
        if (a == null) return (null, "المشرف غير موجود");
        if (a.Role == AdminRole.MainAdmin && !s.IsMain) return (null, "لا يمكن تعديل المسؤول الرئيسي من هنا");
        if (!s.IsMain && a.Role != AdminRole.Admin) return (null, "ليست لديك صلاحية على هذا الحساب");
        if (a.Id == s.Id) return (null, "لا يمكن تعديل حسابك من هنا");
        return (a, null);
    }

    public async Task<OpResult> SetActiveAsync(int id, bool active, AdminScope s)
    {
        var (a, e) = await Target(id, s); if (a == null) return OpResult.Fail(e!);
        a.IsActive = active; await db.SaveChangesAsync(); return OpResult.Success();
    }

    public async Task<OpResult> ResetPasswordAsync(int id, string? password, AdminScope s)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 6) return OpResult.Fail("كلمة المرور 6 أحرف على الأقل");
        var (a, e) = await Target(id, s); if (a == null) return OpResult.Fail(e!);
        a.PasswordHash = pw.Hash(password); await db.SaveChangesAsync(); return OpResult.Success();
    }

    public async Task<OpResult> AssignBranchAsync(int id, int branchId, AdminScope s)
    {
        if (!await db.Branches.AnyAsync(x => x.Id == branchId)) return OpResult.Fail("اختر الفرع");
        var (a, e) = await Target(id, s); if (a == null) return OpResult.Fail(e!);
        if (a.Role is not (AdminRole.Admin or AdminRole.SparePartsManager or AdminRole.VehicleWarehouseManager)) return OpResult.Fail("استخدم التعديل لتعيين نطاق هذا الدور");
        db.AdminBranches.RemoveRange(await db.AdminBranches.Where(x => x.AdminId == a.Id).ToListAsync());
        db.AdminWarehouses.RemoveRange(await db.AdminWarehouses.Where(x => x.AdminId == a.Id).ToListAsync());
        a.AllBranches = false;
        a.BranchId = branchId; await db.SaveChangesAsync(); return OpResult.Success();
    }
}

public sealed class UserService(AppDbContext db, IPasswordService pw)
{
    private IQueryable<AppUser> Scoped(AdminScope s)
    {
        var q = db.AppUsers.AsNoTracking().AsQueryable();
        if (s.ScopeBranch is int b) q = q.Where(u => u.Branches.Any(x => x.BranchId == b) || u.CreatedByAdminId == s.Id);
        return q;
    }

    public Task<PagedResult<UserRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = Scoped(s);
        if (p.Term is string t) q = q.Where(x => x.Name.Contains(t) || x.PhoneNumber.Contains(t)
            || x.Branches.Any(b => b.Branch!.Name.Contains(t)) || x.Warehouses.Any(w => w.Warehouse!.Name.Contains(t)));
        var rows = q.OrderBy(x => x.Id).Select(x => new UserRow(x.Id, x.Name, x.PhoneNumber,
            x.Branches.Select(b => b.Branch!.Name).ToList(), x.Warehouses.Select(w => w.Warehouse!.Name).ToList(), x.IsActive, x.CreatedAt));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public Task<UserDetails?> GetAsync(int id, AdminScope s) => Scoped(s).Where(x => x.Id == id).Select(x => new UserDetails(x.Id, x.Name, x.PhoneNumber,
        x.Branches.Select(b => b.BranchId).ToList(), x.Warehouses.Select(w => w.WarehouseId).ToList(),
        x.Branches.Select(b => b.Branch!.Name).ToList(), x.Warehouses.Select(w => w.Warehouse!.Name).ToList(), x.IsActive, x.CreatedAt, x.CanPickDuplicateRow)).FirstOrDefaultAsync();

    public Task<List<NamedItem>> AllAsync(AdminScope s) => Scoped(s).Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new NamedItem(x.Id, x.Name, null)).ToListAsync();

    public async Task<Dictionary<string, string>> SaveAsync(int? id, UserInput i, AdminScope s)
    {
        var err = new Dictionary<string, string>();
        var name = i.Name?.Trim(); var phone = AccountPhones.Normalize(i.PhoneNumber);
        if (string.IsNullOrEmpty(name) || name.Length < 2) err["Name"] = "الاسم مطلوب (حرفين على الأقل)";
        if (!Validators.IsIraqiPhone(phone)) err["PhoneNumber"] = Validators.PhoneError;
        else if (await AccountPhones.IsUsedAsync(db, phone, userId: id)) err["PhoneNumber"] = AccountPhones.DuplicateError;
        var needPw = id == null || !string.IsNullOrEmpty(i.Password);
        if (needPw)
        {
            if (string.IsNullOrEmpty(i.Password)) err["Password"] = "كلمة المرور مطلوبة";
            else if (i.Password.Length < 6) err["Password"] = "كلمة المرور 6 أحرف على الأقل";
            if (i.Password != i.ConfirmPassword) err["ConfirmPassword"] = "تأكيد كلمة المرور غير مطابق";
        }
        var branchIds = (i.BranchIds ?? new()).Distinct().ToList();
        var whIds = (i.WarehouseIds ?? new()).Distinct().ToList();
        var allowedB = db.Branches.AsQueryable(); if (s.ScopeBranch is int sb) allowedB = allowedB.Where(x => x.Id == sb);
        var validB = await allowedB.Where(x => branchIds.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        if (branchIds.Count == 0 || validB.Count != branchIds.Count) err["BranchIds"] = "اختر فرع واحد على الأقل";
        var validW = await db.Warehouses.Where(x => whIds.Contains(x.Id) && branchIds.Contains(x.BranchId)).Select(x => x.Id).ToListAsync();
        if (whIds.Count == 0 || validW.Count != whIds.Count) err["WarehouseIds"] = "اختر مستودع واحد على الأقل من الفروع المختارة";
        if (err.Count > 0) return err;

        AppUser? u;
        if (id is int uid)
        {
            if (await GetAsync(uid, s) == null) { err[""] = "المستخدم غير موجود"; return err; }
            u = await db.AppUsers.Include(x => x.Branches).Include(x => x.Warehouses).FirstAsync(x => x.Id == uid);
            db.AppUserBranches.RemoveRange(u.Branches); db.AppUserWarehouses.RemoveRange(u.Warehouses);
        }
        else { u = new AppUser { CreatedByAdminId = s.Id, IsActive = true }; db.AppUsers.Add(u); }
        u.Name = name!; u.PhoneNumber = phone!; u.CanPickDuplicateRow = i.CanPickDuplicateRow;
        if (needPw) u.PasswordHash = pw.Hash(i.Password!);
        foreach (var b in branchIds) db.AppUserBranches.Add(new AppUserBranch { AppUser = u, BranchId = b });
        foreach (var w in whIds) db.AppUserWarehouses.Add(new AppUserWarehouse { AppUser = u, WarehouseId = w });
        await db.SaveChangesAsync();
        return err;
    }

    public async Task<OpResult> SetActiveAsync(int id, bool active, AdminScope s)
    {
        if (await GetAsync(id, s) == null) return OpResult.Fail("المستخدم غير موجود");
        var u = await db.AppUsers.FindAsync(id);
        u!.IsActive = active; await db.SaveChangesAsync(); return OpResult.Success();
    }
}
