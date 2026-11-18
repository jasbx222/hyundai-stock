using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HyundaiStock.Infrastructure.Data;

/// <summary>Additive, versioned development fixtures. Existing records are never reset.</summary>
public static class DemoSeeder
{
    public const string Marker = "demo-live-v1";
    public const string MainPhone = "07790000000";
    public const string CounterPhone = "07790000001";

    public static async Task SeedAsync(AppDbContext db, IPasswordService pw, SeedOptions options, IHostEnvironment env, ILogger log)
    {
        if (!options.Demo) return;
        if (db.Database.IsSqlServer() && env.IsDevelopment())
        {
            // An explicit seed transaction must run inside the configured retry strategy.
            // A fresh context per attempt avoids replaying stale tracked state after rollback.
            var contextOptions = (DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>();
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var attempt = new AppDbContext(contextOptions);
                await SeedCoreAsync(attempt, pw, options, env, log);
            });
        }
        else await SeedCoreAsync(db, pw, options, env, log);
    }

    private static async Task SeedCoreAsync(AppDbContext db, IPasswordService pw, SeedOptions options, IHostEnvironment env, ILogger log)
    {
        if (!options.Demo) return;
        if (!env.IsDevelopment())
        {
            log.LogWarning("Demo seed refused: only Development is allowed.");
            return;
        }
        if (await db.Sessions.AnyAsync(x => x.StoredFileName == Marker)) return;
        if (string.IsNullOrWhiteSpace(options.DemoPassword) || options.DemoPassword.Length < 8)
            throw new InvalidOperationException("Seed:DemoPassword must contain at least 8 characters.");

        // A collision aborts the entire seed rather than resetting someone else's password or scope.
        var phones = Enumerable.Range(0, 12).Select(i => $"077900000{i:00}").ToList();
        if (await db.Admins.AnyAsync(x => phones.Contains(x.PhoneNumber)) || await db.AppUsers.AnyAsync(x => phones.Contains(x.PhoneNumber)))
            throw new InvalidOperationException("Demo phone range 07790000000-07790000011 is already used. No existing account was changed.");
        var existingLive = await db.Sessions.AnyAsync(x => x.Status == SessionStatus.InProgress);
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        var names = new[] { "المنصور", "الكرادة", "زيونة", "البصرة", "أربيل" };
        var branches = new List<Branch>();
        var warehouses = new List<Warehouse>();
        for (var b = 0; b < names.Length; b++)
        {
            var branch = await db.Branches.FirstOrDefaultAsync(x => x.Name == names[b]);
            if (branch == null)
            {
                var number = 901 + b;
                if (await db.Branches.AnyAsync(x => x.Number == number))
                    throw new InvalidOperationException($"Demo branch number {number} is already used.");
                branch = new Branch { Name = names[b], Number = number };
                db.Branches.Add(branch);
                await db.SaveChangesAsync();
            }
            branches.Add(branch);
            for (var w = 0; w < 2; w++)
            {
                var number = 9011 + b * 10 + w;
                var name = $"مخزن {names[b]} التجريبي - {(w == 0 ? "قطع الغيار" : "المركبات")}";
                if (await db.Warehouses.AnyAsync(x => x.Number == number || (x.BranchId == branch.Id && x.Name == name)))
                    throw new InvalidOperationException($"Demo warehouse {number} is already used. Existing data is unchanged.");
                var warehouse = new Warehouse { Name = name, Number = number, BranchId = branch.Id, Type = w == 0 ? WarehouseType.Type1 : WarehouseType.Type2 };
                db.Warehouses.Add(warehouse);
                warehouses.Add(warehouse);
            }
        }
        await db.SaveChangesAsync();
        Admin MakeAdmin(int n, string name, AdminRole role, Branch? branch = null)
        {
            var admin = new Admin { Name = name + " (تجريبي)", PhoneNumber = phones[n], PasswordHash = pw.Hash(options.DemoPassword), Role = role, BranchId = branch?.Id };
            db.Admins.Add(admin);
            return admin;
        }
        var main = MakeAdmin(0, "المسؤول الرئيسي", AdminRole.MainAdmin);
        MakeAdmin(2, "أدمن المنصور", AdminRole.Admin, branches[0]);
        var manager = MakeAdmin(3, "مدير مخزن المنصور", AdminRole.WarehouseManager);
        var spare = MakeAdmin(4, "مدير القطع", AdminRole.SparePartsManager, branches[0]);
        var accountant = MakeAdmin(5, "محاسب المنصور", AdminRole.Accountant);
        MakeAdmin(6, "سوبر أدمن", AdminRole.SuperAdmin);
        var otherAccountant = MakeAdmin(7, "محاسب البصرة", AdminRole.Accountant);
        var counters = new List<AppUser>();
        for (var b = 0; b < 5; b++)
        {
            var n = b == 0 ? 1 : b + 7;
            var user = new AppUser { Name = (b == 0 ? "أحمد الجرد" : new[] { "", "علي", "حسين", "مصطفى", "آرام" }[b]) + $" - {names[b]} (تجريبي)", PhoneNumber = phones[n], PasswordHash = pw.Hash(options.DemoPassword) };
            db.AppUsers.Add(user);
            counters.Add(user);
        }
        await db.SaveChangesAsync();
        db.AdminWarehouses.AddRange(warehouses.Take(2).Select(w => new AdminWarehouse { AdminId = manager.Id, WarehouseId = w.Id }));
        db.AdminBranches.AddRange(new AdminBranch { AdminId = accountant.Id, BranchId = branches[0].Id }, new AdminBranch { AdminId = otherAccountant.Id, BranchId = branches[3].Id });
        for (var b = 0; b < 5; b++)
        {
            db.AppUserBranches.Add(new AppUserBranch { AppUserId = counters[b].Id, BranchId = branches[b].Id });
            db.AppUserWarehouses.AddRange(warehouses.Skip(b * 2).Take(2).Select(w => new AppUserWarehouse { AppUserId = counters[b].Id, WarehouseId = w.Id }));
        }
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var active = cycle == 2 && !existingLive;
            var year = now.Year - 2 + cycle;
            while (await db.Sessions.AnyAsync(x => x.Year == year)) year++;
            if (year > 2100) throw new InvalidOperationException("No free demo session year is available.");
            var started = active ? now.AddDays(-2) : now.AddYears(Math.Min(0, year - now.Year)).AddDays(-60 + cycle * 20);
            var session = new StocktakingSession { Year = year, StartedByAdminId = main.Id, StartedAt = started, CreatedAt = started,
                EndedAt = active ? null : started.AddDays(3), EndedByAdminId = active ? null : main.Id,
                Status = active ? SessionStatus.InProgress : SessionStatus.Completed,
                FileName = $"جرد تجريبي - {cycle + 1}.xlsx", StoredFileName = cycle == 2 ? Marker : $"{Marker}-history-{cycle}" };
            db.Sessions.Add(session);
            await db.SaveChangesAsync();
            var allParts = new List<InventoryPart>();
            for (var w = 0; w < warehouses.Count; w++)
            for (var n = 0; n < Catalog.Length; n++)
            {
                var item = Catalog[n];
                // Leave the first five scan cards untouched in the live session.
                var counted = !active || n >= 5 && n % 4 != 0;
                var system = n < 5 ? new[] { 9, 6, 2, 8, 10 }[n] : 5 + (n * 7 + w * 3) % 80;
                var location = n < 5 ? new[] { "A-01", "B-12", "C-03", "D-07", "F-02" }[n] : $"{(char)('A' + n % 6)}-{n % 20 + 1:00}";
                var part = new InventoryPart { SessionId = session.Id, BranchId = warehouses[w].BranchId, WarehouseId = warehouses[w].Id,
                    PartNumber = item.Number, Description = item.Name, Price = item.Price, SystemQuantity = system, SystemLocation = location,
                    CountedQuantity = counted ? Math.Max(0, system + n % 5 - 2) : null, CountedLocation = counted ? location : null,
                    CountedByUserId = counted ? counters[w / 2].Id : null, CountedAt = counted ? started.AddHours(2 + n / 10.0) : null,
                    Latitude = counted ? Coordinates[w / 2].Lat : null, Longitude = counted ? Coordinates[w / 2].Lng : null, CreatedAt = started };
                db.InventoryParts.Add(part);
                allParts.Add(part);
                if (counted) part.ChangeLogs.Add(new PartChangeLog { Field = "الكمية المحسوبة", NewValue = part.CountedQuantity.ToString(), ChangedBy = counters[w / 2].Name, Source = "جرد تجريبي", CreatedAt = started.AddHours(3) });
            }
            await db.SaveChangesAsync();
            // Valid synthetic Hyundai VINs, including an ISO 3779 check digit; not real vehicle identities.
            for (var b = 0; b < 5; b++)
            for (var n = 0; n < 6; n++)
            {
                var vin = Vin(b * 10 + n + 1);
                db.ExpectedVins.Add(new ExpectedVin { SessionId = session.Id, Vin = vin, Model = new[] { "Elantra 2025", "Tucson 2025", "Santa Fe 2024" }[n % 3], BranchId = branches[b].Id, WarehouseId = warehouses[b * 2 + 1].Id });
                if (!active || n >= 2) db.ScannedVins.Add(new ScannedVin { SessionId = session.Id, Vin = vin, BranchId = branches[b].Id, WarehouseId = warehouses[b * 2 + 1].Id, UserId = counters[b].Id, Latitude = Coordinates[b].Lat, Longitude = Coordinates[b].Lng, CreatedAt = started.AddHours(5 + n) });
            }
            for (var n = 0; n < 4; n++)
            {
                var state = n == 0 ? NewPartRequestStatus.PendingSparePartsManager : n == 1 ? NewPartRequestStatus.PendingAccountant : n == 2 ? NewPartRequestStatus.Approved : NewPartRequestStatus.ReturnedToEmployee;
                if (!active && n != 2) state = NewPartRequestStatus.SessionClosed;
                var request = new NewPartRequest { PartNumber = $"DEMO-NEW-{n + 1}", NormalizedPartNumber = $"DEMONEW{n + 1}", Description = "[صيانة] جلدة باب / Door seal - تجريبي", Quantity = 2 + n, Location = "N-01", SessionId = session.Id, BranchId = branches[0].Id, WarehouseId = warehouses[0].Id, RequestedByUserId = counters[0].Id, Latitude = Coordinates[0].Lat, Longitude = Coordinates[0].Lng, Status = state, CreatedAt = started.AddHours(6), UpdatedAt = started.AddHours(9),
                    SparePartsApprovedByAdminId = n is 1 or 2 ? spare.Id : null, SparePartsApprovedAt = n is 1 or 2 ? started.AddHours(8) : null,
                    AccountantApprovedByAdminId = n == 2 ? accountant.Id : null, AccountantApprovedAt = n == 2 ? started.AddHours(9) : null, ReturnReason = n == 3 ? "يرجى تصوير رقم القطعة والتأكد من الوصف" : null };
                if (n == 2)
                {
                    request.InventoryPart = new InventoryPart { SessionId = session.Id, PartNumber = request.PartNumber, Description = request.Description, SystemQuantity = 0, CountedQuantity = request.Quantity, CountedLocation = request.Location, CountedByUserId = counters[0].Id, CountedAt = started.AddHours(9), BranchId = branches[0].Id, WarehouseId = warehouses[0].Id, Latitude = request.Latitude, Longitude = request.Longitude };
                    request.InventoryPart.ChangeLogs.Add(new PartChangeLog { Field = "إنشاء القطعة", NewValue = request.Quantity.ToString(), ChangedBy = accountant.Name, Source = "New Part Request", CreatedAt = started.AddHours(9) });
                }
                db.NewPartRequests.Add(request);
                await db.SaveChangesAsync();
                void NewAudit(string action, string? from, string to, int? admin, DateTime at) => db.WorkflowAudits.Add(new WorkflowAudit { Subject = AuditActions.NewPart, SubjectId = request.Id, NewPartRequestId = request.Id, Action = action, FromStatus = from, ToStatus = to, ActorAdminId = admin, ActorUserId = admin == null ? counters[0].Id : null, ActorRole = admin == null ? "Counter" : admin == spare.Id ? "SparePartsManager" : "Accountant", SessionId = session.Id, BranchId = branches[0].Id, WarehouseId = warehouses[0].Id, InventoryPartId = request.InventoryPartId, CreatedAt = at });
                NewAudit(AuditActions.Submitted, null, "PendingSparePartsManager", null, started.AddHours(6));
                if (n is 1 or 2) NewAudit(AuditActions.FirstApproved, "PendingSparePartsManager", "PendingAccountant", spare.Id, started.AddHours(8));
                if (n >= 2 || !active) NewAudit(n == 2 ? AuditActions.FinalApproved : n == 3 && active ? AuditActions.Returned : AuditActions.SessionClosed, n is 1 or 2 ? "PendingAccountant" : "PendingSparePartsManager", state.ToString(), n == 2 ? accountant.Id : spare.Id, started.AddHours(9));
            }
            db.RecountRequests.Add(new RecountRequest { InventoryPartId = allParts[20].Id, AssignedUserId = counters[0].Id, RequestedByAdminId = manager.Id, Status = active ? RecountStatus.Pending : RecountStatus.Completed, ResultQuantity = active ? null : allParts[20].CountedQuantity, CompletedAt = active ? null : started.AddHours(10), CreatedAt = started.AddHours(8) });
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        log.LogWarning("Development demo data added. Existing accounts and sessions were preserved. Demo active session created: {Active}", !existingLive);
    }

    public static string Vin(int serial)
    {
        var chars = $"KMHCT41D0NU{serial:000000}".ToCharArray();
        const string alphabet = "0123456789ABCDEFGHJKLMNPRSTUVWXYZ";
        int[] values = [0,1,2,3,4,5,6,7,8,9,1,2,3,4,5,6,7,8,1,2,3,4,5,7,9,2,3,4,5,6,7,8,9];
        int[] weights = [8,7,6,5,4,3,2,10,0,9,8,7,6,5,4,3,2];
        var sum = chars.Select((c, n) => values[alphabet.IndexOf(c)] * weights[n]).Sum() % 11;
        chars[8] = sum == 10 ? 'X' : (char)('0' + sum);
        return new string(chars);
    }

    private static readonly (double Lat, double Lng)[] Coordinates = [(33.3152,44.3661),(33.3031,44.4212),(33.3288,44.4701),(30.5085,47.7804),(36.1911,44.0092)];
    private sealed record Item(string Number, string Name, decimal Price);
    // Representative catalogue fixtures and indicative IQD prices, not a sales quotation.
    private static readonly Item[] Catalog =
    [
        new("58101D3A01", "[فرامل] فحمات أمامية / Front brake pads", 85000),
        new("2435508000", "[محرك] صمام توقيت / Timing control valve", 125000),
        new("230602F930", "[محرك] حلقات مكبس / Piston ring set", 95000),
        new("58310BWA40", "[فرامل] فحمات خلفية / Rear brake pads", 65000),
        new("S2630035505", "[فلاتر] فلتر زيت / Oil filter", 12000),
        new("28113F2000", "[فلاتر] فلتر هواء النترا / Elantra air filter", 22000),
        new("97133D1000", "[فلاتر] فلتر مكيف / Cabin filter", 18000),
        new("1885510060", "[إشعال] بوجي / Spark plug", 15000),
        new("252122B000", "[سيور] سير محرك / Drive belt", 35000),
        new("254302S000", "[تبريد] قربة ماء / Coolant reservoir", 45000),
        new("253102E850", "[تبريد] راديتر / Radiator", 230000),
        new("251002B700", "[تبريد] مضخة ماء / Water pump", 115000),
        new("256202B000", "[تبريد] ثرموستات / Thermostat", 42000),
        new("52910D3100", "[عجلات] جنط توسان / Tucson wheel", 275000),
        new("51720A5000", "[عجلات] بيرنغ أمامي / Front wheel bearing", 95000),
        new("54500D3000", "[تعليق] ذراع أمامي يسار / Left control arm", 155000),
        new("54501D3000", "[تعليق] ذراع أمامي يمين / Right control arm", 155000),
        new("54651D3000", "[تعليق] مساعد أمامي يسار / Left front strut", 180000),
        new("54661D3000", "[تعليق] مساعد أمامي يمين / Right front strut", 180000),
        new("55311D3000", "[تعليق] مساعد خلفي / Rear shock absorber", 110000),
        new("54830D3000", "[تعليق] رابط توازن / Stabilizer link", 38000),
        new("57724D3000", "[توجيه] نهاية مقود / Tie rod end", 45000),
        new("49500D3000", "[نقل حركة] عكس أمامي يسار / Left drive shaft", 265000),
        new("49501D3000", "[نقل حركة] عكس أمامي يمين / Right drive shaft", 265000),
        new("21810D3000", "[محرك] قاعدة محرك / Engine mount", 95000),
        new("392102B210", "[حساسات] حساس اوكسجين / Oxygen sensor", 125000),
        new("393502B030", "[حساسات] حساس كرنك / Crankshaft sensor", 68000),
        new("391802B000", "[حساسات] حساس حرارة / Temperature sensor", 35000),
        new("311111R000", "[وقود] مضخة بنزين / Fuel pump", 165000),
        new("353102B020", "[وقود] بخاخ وقود / Fuel injector", 78000),
        new("92101D3000", "[إنارة] مصباح أمامي يسار / Left headlamp", 320000),
        new("92102D3000", "[إنارة] مصباح أمامي يمين / Right headlamp", 320000),
        new("92401D3000", "[إنارة] مصباح خلفي يسار / Left tail lamp", 185000),
        new("92402D3000", "[إنارة] مصباح خلفي يمين / Right tail lamp", 185000),
        new("98360D3000", "[هيكل] مساحة زجاج / Wiper blade", 28000),
        new("87610D3000", "[هيكل] مرآة يسار / Left mirror", 210000),
        new("87620D3000", "[هيكل] مرآة يمين / Right mirror", 210000),
        new("86511D3000", "[هيكل] صدام أمامي / Front bumper cover", 265000),
        new("86351D3000", "[هيكل] شبك أمامي / Front grille", 145000),
        new("97606D3000", "[تكييف] مكثف مكيف / AC condenser", 240000),
    ];
}
