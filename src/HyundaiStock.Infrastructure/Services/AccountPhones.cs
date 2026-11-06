using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Services;

public static class AccountPhones
{
    public const string DuplicateError = "رقم الهاتف مستخدم لحساب آخر (مشرف أو مستخدم). استخدم رقمًا مختلفًا";

    // Canonical Iraqi local format; do not discard arbitrary invalid characters.
    public static string Normalize(string? value)
    {
        var digits = new string((value ?? "").Trim().Select(c => c switch
        {
            >= '\u0660' and <= '\u0669' => (char)('0' + c - '\u0660'),
            >= '\u06f0' and <= '\u06f9' => (char)('0' + c - '\u06f0'),
            _ => c
        }).Where(c => !char.IsWhiteSpace(c) && c is not ('-' or '(' or ')')).ToArray());
        if (digits.StartsWith("+964")) return "0" + digits[4..];
        if (digits.StartsWith("00964")) return "0" + digits[5..];
        if (digits.StartsWith("964") && digits.Length == 13) return "0" + digits[3..];
        return digits;
    }

    public static async Task<bool> IsUsedAsync(AppDbContext db, string phone, int? adminId = null, int? userId = null)
    {
        // Compare normalized legacy values too, including disabled accounts.
        var admins = await db.Admins.AsNoTracking().Where(a => a.Id != (adminId ?? 0)).Select(a => a.PhoneNumber).ToListAsync();
        if (admins.Any(p => Normalize(p) == phone)) return true;
        var users = await db.AppUsers.AsNoTracking().Where(u => u.Id != (userId ?? 0)).Select(u => u.PhoneNumber).ToListAsync();
        return users.Any(p => Normalize(p) == phone);
    }
}
