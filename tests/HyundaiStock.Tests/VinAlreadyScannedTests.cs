using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HyundaiStock.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

/// <summary>A VIN scanned once is refused for everyone after, like a counted part: the message names the employee and nothing else is saved.</summary>
public sealed class VinAlreadyScannedTests
{
    private static async Task<(HttpClient Client, int Warehouse, string Name)> Mobile(ApiFactory factory, string phone)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { phoneNumber = phone, password = "Test1234" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        client.DefaultRequestHeaders.Authorization = new("Bearer", user.GetProperty("accessToken").GetString());
        var me = (await client.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("data");
        return (client, me.GetProperty("warehouses")[0].GetProperty("id").GetInt32(), me.GetProperty("name").GetString()!);
    }

    private static object Scan(string vin, int warehouse) => new { vin, warehouseId = warehouse, latitude = 33.3, longitude = 44.4 };

    [Fact]
    public async Task Scanning_a_vin_already_scanned_names_the_employee_and_saves_nothing()
    {
        using var factory = new ApiFactory { Demo = true };
        var (a, whA, nameA) = await Mobile(factory, DemoSeeder.CounterPhone);
        var (b, whB, _) = await Mobile(factory, "07790000008");
        var vin = DemoSeeder.Vin(900);

        // Not scanned yet: the lookup says so and a scan succeeds.
        var fresh = (await b.GetFromJsonAsync<JsonElement>($"/api/v1/vins/lookup?vin={vin}")).GetProperty("data");
        Assert.False(fresh.GetProperty("alreadyScanned").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await a.PostAsJsonAsync("/api/v1/vins/scan", Scan(vin, whA))).StatusCode);

        // Another employee: the lookup warns with the name, and the scan is refused with the same name.
        var seen = (await b.GetFromJsonAsync<JsonElement>($"/api/v1/vins/lookup?vin={vin}")).GetProperty("data");
        Assert.True(seen.GetProperty("alreadyScanned").GetBoolean());
        Assert.Equal(nameA, seen.GetProperty("scannedBy").GetString());
        Assert.False(seen.GetProperty("scannedByMe").GetBoolean());
        var refused = await b.PostAsJsonAsync("/api/v1/vins/scan", Scan(vin, whB));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal($"هذا الشاصي ممسوح من قبل الموظف {nameA}", body.GetProperty("message").GetString());
        Assert.Equal(nameA, body.GetProperty("data").GetProperty("scannedBy").GetString());
        Assert.False(body.GetProperty("data").GetProperty("scannedByMe").GetBoolean());

        // The employee who scanned it gets a different note, and still cannot save it twice.
        var mine = (await a.GetFromJsonAsync<JsonElement>($"/api/v1/vins/lookup?vin={vin}")).GetProperty("data");
        Assert.True(mine.GetProperty("scannedByMe").GetBoolean());
        var again = await a.PostAsJsonAsync("/api/v1/vins/scan", Scan(vin, whA));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        var againBody = await again.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("أنت مسحت هذا الشاصي مسبقًا في هذه الجلسة", againBody.GetProperty("message").GetString());
        Assert.True(againBody.GetProperty("data").GetProperty("scannedByMe").GetBoolean());

        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().ScannedVins.CountAsync(x => x.Vin == vin));
    }
}
