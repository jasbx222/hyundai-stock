using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HyundaiStock.Infrastructure.Services;

public interface IStockNotifier
{
    Task SessionStartedAsync(int sessionId);
    Task RecountAssignedAsync(int userId, int recountId, string partNumber);
    Task QuantityRequestedAsync(IReadOnlyCollection<int> userIds, int requestId, string partNumber);
}

/// <summary>
/// Sends FCM HTTP v1 notifications. Missing credentials or a provider error are logged and
/// never reported as delivered, and never fail the business operation.
/// </summary>
public sealed class StockNotifier(AppDbContext db, ILogger<StockNotifier> log, IConfiguration cfg, IHttpClientFactory http) : IStockNotifier
{
    private static readonly SemaphoreSlim TokenGate = new(1, 1);
    private static string? AccessToken;
    private static DateTime AccessUntil;
    public Task SessionStartedAsync(int sessionId) => Guard(async () =>
    {
        var warehouses = await db.InventoryParts.AsNoTracking().Where(x => x.SessionId == sessionId && x.WarehouseId != null).Select(x => x.WarehouseId!.Value)
            .Union(db.ExpectedVins.AsNoTracking().Where(x => x.SessionId == sessionId && x.WarehouseId != null).Select(x => x.WarehouseId!.Value))
            .Distinct().ToListAsync();
        var users = await db.AppUserWarehouses.AsNoTracking().Where(x => warehouses.Contains(x.WarehouseId) && x.AppUser!.IsActive).Select(x => x.AppUserId).Distinct().ToListAsync();
        await SendAsync(users, "جلسة جرد جديدة", "بدأت جلسة جرد ضمن مخازنك", "session", sessionId);
    });

    public Task RecountAssignedAsync(int userId, int recountId, string partNumber) =>
        Guard(() => SendAsync(new[] { userId }, "طلب إعادة عد", "طُلب منك إعادة عد القطعة " + partNumber, "recount", recountId));

    public Task QuantityRequestedAsync(IReadOnlyCollection<int> userIds, int requestId, string partNumber) =>
        Guard(() => SendAsync(userIds, "طلب تعديل كمية", "وصل طلب تعديل كمية للقطعة " + partNumber, "quantity", requestId));

    private async Task Guard(Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex) { log.LogWarning(ex, "Push notification failed"); }
    }

    private sealed record FcmAccount(string ProjectId, string ClientEmail, string PrivateKey);

    private FcmAccount? Account()
    {
        var raw = Environment.GetEnvironmentVariable("FCM_SERVICE_ACCOUNT_JSON");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var text = raw.Trim();
            try
            {
                if (!text.StartsWith('{') && File.Exists(text)) text = File.ReadAllText(text);
                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                var project = root.TryGetProperty("project_id", out var p) ? p.GetString() : null;
                var email = root.TryGetProperty("client_email", out var e) ? e.GetString() : null;
                var key = root.TryGetProperty("private_key", out var k) ? k.GetString() : null;
                if (!string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(key))
                    return new FcmAccount(project, email, key);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Push skipped: FCM service account could not be read");
                return null;
            }
        }
        static string? Pick(string? env, string? configured) =>
            !string.IsNullOrWhiteSpace(env) ? env.Trim() : string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
        var projectId = Pick(Environment.GetEnvironmentVariable("FCM_PROJECT_ID"), cfg["Notifications:FcmProjectId"]);
        var clientEmail = Pick(Environment.GetEnvironmentVariable("FCM_CLIENT_EMAIL"), cfg["Notifications:FcmClientEmail"]);
        var privateKey = Pick(Environment.GetEnvironmentVariable("FCM_PRIVATE_KEY"), cfg["Notifications:FcmPrivateKey"]);
        if (projectId == null || clientEmail == null || privateKey == null)
        {
            log.LogWarning("Push skipped: FCM HTTP v1 credentials are not configured");
            return null;
        }
        return new FcmAccount(projectId, clientEmail, privateKey.Replace("\\n", "\n", StringComparison.Ordinal));
    }

    private async Task<string?> AccessAsync(FcmAccount account)
    {
        if (AccessToken != null && AccessUntil > DateTime.UtcNow.AddMinutes(1)) return AccessToken;
        await TokenGate.WaitAsync();
        try
        {
            if (AccessToken != null && AccessUntil > DateTime.UtcNow.AddMinutes(1)) return AccessToken;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"typ\":\"JWT\"}"));
            var claims = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
            {
                iss = account.ClientEmail,
                scope = "https://www.googleapis.com/auth/firebase.messaging",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = now + 3600
            })));
            var signingInput = Encoding.ASCII.GetBytes(header + "." + claims);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(account.PrivateKey);
            var signature = Base64Url(rsa.SignData(signingInput, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            var client = http.CreateClient("fcm");
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                ["assertion"] = header + "." + claims + "." + signature
            });
            using var res = await client.PostAsync("https://oauth2.googleapis.com/token", body);
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Push skipped: FCM token request failed with status {Status}", (int)res.StatusCode);
                return null;
            }
            using var doc = JsonDocument.Parse(text);
            AccessToken = doc.RootElement.GetProperty("access_token").GetString();
            var seconds = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
            AccessUntil = DateTime.UtcNow.AddSeconds(seconds);
            return AccessToken;
        }
        finally { TokenGate.Release(); }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private async Task SendAsync(IReadOnlyCollection<int> userIds, string title, string body, string type, int id)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) { log.LogInformation("Push skipped: nobody to notify for {Type} {Id}", type, id); return; }
        var account = Account();
        if (account == null) return;
        var token = await AccessAsync(account);
        if (string.IsNullOrEmpty(token)) return;
        var devices = await db.DeviceRegistrations.Where(x => ids.Contains(x.AppUserId) && x.IsActive).Select(x => new { x.Id, x.Token }).ToListAsync();
        if (devices.Count == 0) { log.LogInformation("Push skipped: no active device for {Type} {Id}", type, id); return; }
        var client = http.CreateClient("fcm");
        foreach (var device in devices)
        {
            var payload = JsonSerializer.Serialize(new
            {
                message = new
                {
                    token = device.Token,
                    notification = new { title, body },
                    data = new Dictionary<string, string> { ["type"] = type, ["id"] = id.ToString() },
                    android = new { priority = "HIGH" }
                }
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/v1/projects/" + account.ProjectId + "/messages:send");
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var res = await client.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if (res.IsSuccessStatusCode) continue;
            var code = ErrorCode(text);
            log.LogWarning("Push device {DeviceId} failed with status {Status} code {Code}", device.Id, (int)res.StatusCode, code);
            if (code is "UNREGISTERED" or "NOT_FOUND")
                await db.DeviceRegistrations.Where(x => x.Id == device.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
        }
    }

    private static string? ErrorCode(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return null;
            if (error.TryGetProperty("details", out var details))
            {
                foreach (var item in details.EnumerateArray())
                    if (item.TryGetProperty("errorCode", out var code)) return code.GetString();
            }
            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException) { return null; }
    }
}
