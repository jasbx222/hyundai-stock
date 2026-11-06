using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public sealed record BranchRow(int Id, string Name, int Warehouses, int? Number = null);
public sealed record WarehouseRow(int Id, string Name, WarehouseType Type, int BranchId, string BranchName, int? Number = null);
public sealed record NamedItem(int Id, string Name, int? ParentId = null);

public sealed class BranchService(AppDbContext db)
{
    public async Task<PagedResult<BranchRow>> ListAsync(PageQuery p, AdminScope s)
    {
        var q = db.Branches.AsNoTracking().AsQueryable();
        if (s.ScopeBranch is int b) q = q.Where(x => x.Id == b);
        if (p.Term is string t) q = q.Where(x => x.Name.Contains(t) || x.Number.ToString() == t);
        var rows = q.OrderBy(x => x.Id).Select(x => new BranchRow(x.Id, x.Name, x.Warehouses.Count(), x.Number));
        return await Task.FromResult(Paging.Build(rows, p));
    }

    public Task<List<NamedItem>> AllAsync(AdminScope? s = null)
    {
        var q = db.Branches.AsNoTracking().AsQueryable();
        if (s is { IsUnrestricted: false }) { var ids = s.Branches.ToList(); q = q.Where(x => ids.Contains(x.Id)); }
        return q.OrderBy(x => x.Name).Select(x => new NamedItem(x.Id, x.Name, null)).ToListAsync();
    }

    public Task<Branch?> GetAsync(int id) => db.Branches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    public async Task<OpResult> SaveAsync(int? id, string? name, int? number = null)
    {
        if (number == null) return OpResult.Fail("رقم الفرع مطلوب");
        if (number is int n && (n < 1 || n > 9999)) return OpResult.Fail("رقم الفرع غير صالح");
        if (number != null && await db.Branches.AnyAsync(x => x.Number == number && x.Id != (id ?? 0))) return OpResult.Fail($"رقم الفرع {number} مستخدم لفرع آخر");
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2) return OpResult.Fail("اسم الفرع يجب أن يتكون من حرفين على الأقل");
        if (name.Length > 150) return OpResult.Fail("اسم الفرع طويل");
        if (await db.Branches.AnyAsync(x => x.Name == name && x.Id != (id ?? 0))) return OpResult.Fail("يوجد فرع بنفس الاسم");
        Branch? e;
        if (id is int i) { e = await db.Branches.FindAsync(i); if (e == null) return OpResult.Fail("الفرع غير موجود"); }
        else { e = new Branch(); db.Branches.Add(e); }
        e.Name = name; e.Number = number;
        await db.SaveChangesAsync();
        return OpResult.Success(e.Id);
    }

    public async Task<OpResult> DeleteAsync(int id)
    {
        var e = await db.Branches.FindAsync(id);
        if (e == null) return OpResult.Fail("الفرع غير موجود");
        if (await db.Warehouses.AnyAsync(x => x.BranchId == id)) return OpResult.Fail("لا يمكن حذف الفرع لوجود مخازن مرتبطة به");
        if (await db.Admins.AnyAsync(x => x.BranchId == id)) return OpResult.Fail("لا يمكن حذف الفرع لوجود مشرفين مرتبطين به");
        if (await db.AppUserBranches.AnyAsync(x => x.BranchId == id)) return OpResult.Fail("لا يمكن حذف الفرع لوجود مستخدمين مرتبطين به");
        if (await db.InventoryParts.AnyAsync(x => x.BranchId == id) || await db.ScannedVins.AnyAsync(x => x.BranchId == id) || await db.ExpectedVins.AnyAsync(x => x.BranchId == id))
            return OpResult.Fail("لا يمكن حذف الفرع لوجود بيانات جرد مرتبطة به");
        db.Branches.Remove(e);
        await db.SaveChangesAsync();
        return OpResult.Success();
    }
}

public sealed class WarehouseService(AppDbContext db)
{
    public Task<PagedResult<WarehouseRow>> ListAsync(PageQuery p, int? branchId, AdminScope s)
    {
        var q = db.Warehouses.AsNoTracking().AsQueryable();
        if (s.ScopeBranch is int b) q = q.Where(x => x.BranchId == b);
        if (branchId is int f) q = q.Where(x => x.BranchId == f);
        if (p.Term is string t) q = q.Where(x => x.Name.Contains(t) || x.Branch!.Name.Contains(t) || x.Number.ToString() == t);
        var rows = q.OrderBy(x => x.Id).Select(x => new WarehouseRow(x.Id, x.Name, x.Type, x.BranchId, x.Branch!.Name, x.Number));
        return Task.FromResult(Paging.Build(rows, p));
    }

    public Task<List<NamedItem>> AllAsync(AdminScope? s = null)
    {
        var q = db.Warehouses.AsNoTracking().AsQueryable();
        if (s is { IsUnrestricted: false, Role: AdminRole.WarehouseManager }) { var ids = s.Warehouses.ToList(); q = q.Where(x => ids.Contains(x.Id)); }
        else if (s is { IsUnrestricted: false }) { var ids = s.Branches.ToList(); q = q.Where(x => ids.Contains(x.BranchId)); }
        return q.OrderBy(x => x.BranchId).ThenBy(x => x.Name).Select(x => new NamedItem(x.Id, x.Name, x.BranchId)).ToListAsync();
    }

    public Task<Warehouse?> GetAsync(int id) => db.Warehouses.AsNoTracking().Include(x => x.Branch).FirstOrDefaultAsync(x => x.Id == id);

    public async Task<OpResult> SaveAsync(int? id, string? name, int? type, int branchId, int? number = null)
    {
        if (number == null) return OpResult.Fail("رقم المخزن مطلوب");
        if (number is int n && (n < 1 || n > 9999)) return OpResult.Fail("رقم المخزن غير صالح");
        if (number != null && await db.Warehouses.AnyAsync(x => x.Number == number && x.Id != (id ?? 0))) return OpResult.Fail($"رقم المخزن {number} مستخدم لمخزن آخر");
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length < 2) return OpResult.Fail("اسم المخزن يجب أن يتكون من حرفين على الأقل");
        if (name.Length > 150) return OpResult.Fail("اسم المخزن طويل");
        if (type is not (null or 1 or 2)) return OpResult.Fail("اختر نوع المخزن");
        if (!await db.Branches.AnyAsync(x => x.Id == branchId)) return OpResult.Fail("اختر الفرع");
        if (await db.Warehouses.AnyAsync(x => x.BranchId == branchId && x.Name == name && x.Id != (id ?? 0))) return OpResult.Fail("يوجد مخزن بالاسم نفسه في هذا الفرع");
        Warehouse? e;
        if (id is int i) { e = await db.Warehouses.FindAsync(i); if (e == null) return OpResult.Fail("المخزن غير موجود"); }
        else { e = new Warehouse(); db.Warehouses.Add(e); }
        if (id != null && e.BranchId != branchId && (await db.InventoryParts.AnyAsync(x => x.WarehouseId == e.Id) || await db.ScannedVins.AnyAsync(x => x.WarehouseId == e.Id)))
            return OpResult.Fail("لا يمكن تغيير فرع المخزن لوجود بيانات جرد مرتبطة به");
        e.Name = name; e.Type = type is int suppliedType ? (WarehouseType)suppliedType : id == null ? WarehouseType.Type1 : e.Type; e.BranchId = branchId; e.Number = number;
        await db.SaveChangesAsync();
        return OpResult.Success(e.Id);
    }

    public async Task<OpResult> DeleteAsync(int id)
    {
        var e = await db.Warehouses.FindAsync(id);
        if (e == null) return OpResult.Fail("المخزن غير موجود");
        if (await db.AppUserWarehouses.AnyAsync(x => x.WarehouseId == id)) return OpResult.Fail("لا يمكن حذف المخزن لوجود مستخدمين مرتبطين به");
        if (await db.InventoryParts.AnyAsync(x => x.WarehouseId == id) || await db.ScannedVins.AnyAsync(x => x.WarehouseId == id) || await db.ExpectedVins.AnyAsync(x => x.WarehouseId == id))
            return OpResult.Fail("لا يمكن حذف المخزن لوجود بيانات جرد مرتبطة به");
        db.Warehouses.Remove(e);
        await db.SaveChangesAsync();
        return OpResult.Success();
    }
}
