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
/// Sends FCM notifications when a server key is configured. Missing credentials or a provider
/// error are logged and never reported as delivered, and never fail the business operation.
/// </summary>
public sealed class StockNotifier(AppDbContext db, ILogger<StockNotifier> log, IConfiguration cfg, IHttpClientFactory http) : IStockNotifier
{
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

    private string? ServerKey()
    {
        var env = Environment.GetEnvironmentVariable("FCM_SERVER_KEY");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        var configured = cfg["Notifications:FcmServerKey"];
        return string.IsNullOrWhiteSpace(configured) ? null : configured.Trim();
    }

    private async Task SendAsync(IReadOnlyCollection<int> userIds, string title, string body, string type, int id)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) { log.LogInformation("Push skipped: nobody to notify for {Type} {Id}", type, id); return; }
        var key = ServerKey();
        if (key == null) { log.LogWarning("Push skipped: FCM_SERVER_KEY is not configured ({Type} {Id})", type, id); return; }
        var devices = await db.DeviceRegistrations.Where(x => ids.Contains(x.AppUserId) && x.IsActive).Select(x => new { x.Id, x.Token }).ToListAsync();
        if (devices.Count == 0) { log.LogInformation("Push skipped: no active device for {Type} {Id}", type, id); return; }
        var client = http.CreateClient("fcm");
        foreach (var chunk in devices.Chunk(100))
        {
            var payload = JsonSerializer.Serialize(new
            {
                registration_ids = chunk.Select(x => x.Token).ToArray(),
                notification = new { title, body },
                data = new Dictionary<string, string> { ["type"] = type, ["id"] = id.ToString() }
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://fcm.googleapis.com/fcm/send");
            req.Headers.TryAddWithoutValidation("Authorization", "key=" + key);
            req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var res = await client.SendAsync(req);
            var text = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Push provider rejected {Type} {Id} with status {Status}", type, id, (int)res.StatusCode);
                continue;
            }
            try
            {
                using var doc = JsonDocument.Parse(text);
                if (!doc.RootElement.TryGetProperty("results", out var results)) continue;
                var n = 0;
                foreach (var item in results.EnumerateArray())
                {
                    if (n >= chunk.Length) break;
                    if (item.TryGetProperty("error", out var error))
                    {
                        var code = error.GetString();
                        log.LogWarning("Push device {DeviceId} failed with {Code}", chunk[n].Id, code);
                        if (code is "NotRegistered" or "InvalidRegistration")
                        {
                            var deviceId = chunk[n].Id;
                            await db.DeviceRegistrations.Where(x => x.Id == deviceId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false).SetProperty(x => x.UpdatedAt, DateTime.UtcNow));
                        }
                    }
                    n++;
                }
            }
            catch (JsonException) { log.LogWarning("Push provider returned an unreadable response for {Type} {Id}", type, id); }
        }
    }
}
