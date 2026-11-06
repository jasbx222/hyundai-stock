using System.Data.Common;
using System.Diagnostics;
using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;
using HyundaiStock.Infrastructure.Data;
using HyundaiStock.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace HyundaiStock.Tests;

public sealed class ApprovalReliabilityTests
{
    [Fact]
    public async Task Busy_database_fails_within_budget_then_can_approve_once_without_partial_changes()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.QuantityCorrections.AsNoTracking().FirstAsync(x => x.ApprovalLevel == ApprovalLevel.First && x.Status == RequestStatus.Pending && x.InventoryPart!.Session!.Status == SessionStatus.InProgress);
        var accountant = await db.Admins.SingleAsync(x => x.PhoneNumber == "07790000005");
        var admin = new AdminScope(accountant.Id, accountant.Name, AdminRole.Accountant, null, null, new[] { request.BranchId!.Value });
        var oldQuantity = await db.InventoryParts.Where(x => x.Id == request.InventoryPartId).Select(x => x.CountedQuantity).SingleAsync();
        Assert.Equal(5, ((SqliteConnection)db.Database.GetDbConnection()).DefaultTimeout);
        await using (var connection = new SqliteConnection(db.Database.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var blocker = await connection.BeginTransactionAsync();
            var timer = Stopwatch.StartNew();
            var result = await scope.ServiceProvider.GetRequiredService<QuantityRequestService>().FinalApproveAsync(request.Id, admin);
            Assert.False(result.Ok);
            Assert.Contains("قاعدة البيانات مشغولة", result.Error);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(9), timer.Elapsed.ToString());
            await blocker.RollbackAsync();
        }
        Assert.Equal(oldQuantity, await db.InventoryParts.AsNoTracking().Where(x => x.Id == request.InventoryPartId).Select(x => x.CountedQuantity).SingleAsync());
        Assert.Equal(0, await db.WorkflowAudits.CountAsync(x => x.Subject == AuditActions.Quantity && x.SubjectId == request.Id && x.Action == AuditActions.FinalApproved));
        var commands = new CommandCounter();
        await using var isolated = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(db.Database.GetConnectionString()!).AddInterceptors(commands).Options);
        var service = new QuantityRequestService(isolated, new SessionLookup(isolated));
        var watch = Stopwatch.StartNew();
        Assert.True((await service.FinalApproveAsync(request.Id, admin)).Ok);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), watch.Elapsed.ToString());
        Assert.InRange(commands.Commands, 3, 10); // PK lookups + conditional update + inventory/log/audit, not N+1.
        Assert.False((await service.FinalApproveAsync(request.Id, admin)).Ok);
        Assert.Equal(request.NewQuantity, await db.InventoryParts.AsNoTracking().Where(x => x.Id == request.InventoryPartId).Select(x => x.CountedQuantity).SingleAsync());
        Assert.Equal(1, await db.WorkflowAudits.CountAsync(x => x.Subject == AuditActions.Quantity && x.SubjectId == request.Id && x.Action == AuditActions.FinalApproved));
    }

    [Fact]
    public async Task Simultaneous_final_approvals_only_apply_once()
    {
        using var factory = new ApiFactory { Demo = true };
        await using var setup = factory.Services.CreateAsyncScope();
        var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.QuantityCorrections.AsNoTracking().FirstAsync(x => x.ApprovalLevel == ApprovalLevel.First && x.Status == RequestStatus.Pending && x.InventoryPart!.Session!.Status == SessionStatus.InProgress);
        var accountant = await db.Admins.SingleAsync(x => x.PhoneNumber == "07790000005");
        var admin = new AdminScope(accountant.Id, accountant.Name, AdminRole.Accountant, null, null, new[] { request.BranchId!.Value });
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<QuantityRequestService>().FinalApproveAsync(request.Id, admin);
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, x => x.Ok);
        Assert.Equal(1, await db.WorkflowAudits.CountAsync(x => x.Subject == AuditActions.Quantity && x.SubjectId == request.Id && x.Action == AuditActions.FinalApproved));
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Commands;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands++; return base.ReaderExecutingAsync(command, eventData, result, cancellationToken); }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Commands++; return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken); }
    }
}
