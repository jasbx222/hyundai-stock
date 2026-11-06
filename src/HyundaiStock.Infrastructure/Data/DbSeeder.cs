using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HyundaiStock.Infrastructure.Data;

public sealed class SeedOptions
{
    public bool Demo { get; set; }
    public string DemoPassword { get; set; } = "Test1234";
    public string? MainAdminName { get; set; }
    public string? MainAdminPhone { get; set; }
    public string? MainAdminPassword { get; set; }
}

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IPasswordService pw, SeedOptions opt, ILogger log)
    {
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
