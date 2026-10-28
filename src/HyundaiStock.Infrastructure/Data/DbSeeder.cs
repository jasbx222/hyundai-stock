using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HyundaiStock.Infrastructure.Data;

public sealed class SeedOptions
{
    public string? MainAdminName { get; set; }
    public string? MainAdminPhone { get; set; }
    public string? MainAdminPassword { get; set; }
}

public static class DbSeeder
{
    /// <summary>Hyundai system branch numbers (BranchNo. in the Excel files).</summary>
    public static readonly (int Number, string Name)[] Branches = [(1, "بغداد"), (2, "بصرة"), (3, "النجف"), (4, "أربيل")];

    public static async Task SeedBranchesAsync(AppDbContext db, ILogger log)
    {
        var changed = 0;
        foreach (var (num, name) in Branches)
        {
            if (await db.Branches.AnyAsync(x => x.Number == num)) continue;
            var byName = await db.Branches.FirstOrDefaultAsync(x => x.Name == name && x.Number == null);
            if (byName != null) byName.Number = num;
            else if (!await db.Branches.AnyAsync(x => x.Name == name)) db.Branches.Add(new Branch { Number = num, Name = name });
            else continue;
            changed++;
        }
        if (changed > 0) { await db.SaveChangesAsync(); log.LogInformation("Seeded {Count} branches.", changed); }
    }

    public static async Task SeedAsync(AppDbContext db, IPasswordService pw, SeedOptions opt, ILogger log)
    {
        await SeedBranchesAsync(db, log);
        if (await db.Admins.AnyAsync(x => x.Role == AdminRole.MainAdmin)) return;
        if (string.IsNullOrWhiteSpace(opt.MainAdminPhone) || string.IsNullOrWhiteSpace(opt.MainAdminPassword))
        {
            log.LogWarning("No main admin exists and Seed:MainAdminPhone / Seed:MainAdminPassword are not configured. Skipping seed.");
            return;
        }
        if (!Validators.IsIraqiPhone(opt.MainAdminPhone)) throw new InvalidOperationException("Seed:MainAdminPhone must be an Iraqi mobile number (07[5-8]XXXXXXXX).");
        if (opt.MainAdminPassword.Length < 6) throw new InvalidOperationException("Seed:MainAdminPassword must be at least 6 characters.");
        db.Admins.Add(new Admin
        {
            Name = string.IsNullOrWhiteSpace(opt.MainAdminName) ? "المسؤول الرئيسي" : opt.MainAdminName.Trim(),
            PhoneNumber = opt.MainAdminPhone.Trim(), PasswordHash = pw.Hash(opt.MainAdminPassword), Role = AdminRole.MainAdmin, IsActive = true
        });
        await db.SaveChangesAsync();
        log.LogInformation("Main admin account seeded.");
    }
}
