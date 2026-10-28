using Microsoft.AspNetCore.Identity;

namespace HyundaiStock.Infrastructure.Services;

public interface IPasswordService { string Hash(string password); bool Verify(string hash, string password); }

public sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> _h = new();
    private static readonly object Subject = new();
    public string Hash(string password) => _h.HashPassword(Subject, password);
    public bool Verify(string hash, string password)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(password)) return false;
        return _h.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
    }
}
