using HyundaiStock.Domain;
using HyundaiStock.Infrastructure.Common;

namespace HyundaiStock.Infrastructure.Services;

/// <summary>
/// Excel files for the dashboard list pages. Each file uses the page's own query (same scope, search and year),
/// returned unpaged, so the file always matches what the page shows.
/// </summary>
public sealed class ListExports(BranchService branches, WarehouseService warehouses, AdminService admins, UserService users,
    PartService parts, VinRequestService vinRequests, SessionService sessions)
{
    private static PageQuery All(string? search) => new(1, 10, search) { Unpaged = true };
    private static double[] Widths(int n, double w = 20) => Enumerable.Repeat(w, n).ToArray();
    private static string Active(bool v) => v ? "مفعل" : "معطل";

    public async Task<ExportFile> UsersAsync(string? search, AdminScope s)
    {
        var rows = (await users.ListAsync(All(search), s)).Items;
        string[] h = { "Name", "Phone", "Branches", "Warehouses", "Count Type", "Status", "Created At" };
        return ExportService.Build("Users", "Users", h, new[] { 24.0, 16, 30, 40, 22, 12, 20 },
            rows.Select(x => new object?[] { x.Name, x.PhoneNumber, string.Join("، ", x.Branches), string.Join("، ", x.Warehouses), Labels.UserCountLabel(x.CountType, x.IsGuest), Active(x.IsActive), x.CreatedAt }));
    }

    public async Task<ExportFile> AdminsAsync(string? search, AdminScope s)
    {
        var rows = (await admins.ListAsync(All(search), s)).Items;
        string[] h = { "Name", "Phone", "Role", "Branch", "Status", "Created At" };
        return ExportService.Build("Admins", "Admins", h, new[] { 24.0, 16, 20, 24, 12, 20 },
            rows.Select(x => new object?[] { x.Name, x.PhoneNumber, Labels.Role(x.Role), x.AllBranches ? "كل الفروع" : x.BranchName ?? "", Active(x.IsActive), x.CreatedAt }));
    }

    public async Task<ExportFile> BranchesAsync(string? search, AdminScope s)
    {
        var rows = (await branches.ListAsync(All(search), s)).Items;
        string[] h = { "BranchNo.", "Name", "Warehouses" };
        return ExportService.Build("Branches", "Branches", h, new[] { 16.48, 28.08, 14 }, rows.Select(x => new object?[] { x.Number, x.Name, x.Warehouses }));
    }

    public async Task<ExportFile> WarehousesAsync(string? search, int? branchId, AdminScope s)
    {
        var rows = (await warehouses.ListAsync(All(search), branchId, s)).Items;
        string[] h = { "WarehouseNo.", "Name", "Branch", "Type" };
        return ExportService.Build("Warehouses", "Warehouses", h, new[] { 16.48, 28.08, 24, 12 }, rows.Select(x => new object?[] { x.Number, x.Name, x.BranchName, Labels.Type(x.Type) }));
    }

    public async Task<ExportFile> RecountsAsync(string? search, int? sessionId, AdminScope s)
    {
        var rows = (await parts.RecountsAsync(All(search), s, sessionId)).Items;
        string[] h = { "Part Number", "Requested By", "Assigned To", "Date", "Status", "Result Quantity" };
        return ExportService.Build("Recounts", "Recounts", h, new[] { 18.0, 22, 22, 20, 14, 16 },
            rows.Select(x => new object?[] { x.PartNumber, x.RequestedBy, x.AssignedTo, x.CreatedAt, x.Status == RecountStatus.Completed ? "مكتمل" : "قيد الانتظار", x.Status == RecountStatus.Completed ? x.ResultQuantity : null }));
    }

    public async Task<ExportFile> VinRequestsAsync(string? search, int? sessionId, AdminScope s)
    {
        var rows = (await vinRequests.ListAsync(All(search), s, sessionId)).Items;
        string[] h = { "Current VIN", "New VIN", "Reason", "Requested By", "Decided By", "Status", "Date", "Decision Date" };
        return ExportService.Build("VIN Requests", "VIN-Requests", h, new[] { 22.0, 22, 30, 22, 22, 14, 20, 20 },
            rows.Select(x => new object?[] { x.CurrentVin, x.NewVin, x.Reason, x.RequestedBy, x.ApprovedBy ?? "", Labels.Qty(x.Status), x.CreatedAt, x.ApprovedAt }));
    }

    public async Task<ExportFile> SessionsAsync(string? search)
    {
        var rows = (await sessions.ListAsync(All(search))).Items;
        string[] h = { "Session", "Type", "Year", "Month", "Status", "Started By", "Started At", "Ended At", "File", "Parts", "VINs" };
        return ExportService.Build("Sessions", "Sessions", h, new[] { 24.0, 10, 10, 8, 14, 22, 20, 20, 36, 10, 10 },
            rows.Select(x => new object?[] { x.Name, Labels.SessionKindLabel(x.Kind), x.Year, x.Month, Labels.Session(x.Status), x.StartedBy, x.StartedAt, x.EndedAt, x.FileName, x.Parts, x.Vins }));
    }
}
