using HyundaiStock.Domain;
using Microsoft.EntityFrameworkCore;

namespace HyundaiStock.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AppUserBranch> AppUserBranches => Set<AppUserBranch>();
    public DbSet<AppUserWarehouse> AppUserWarehouses => Set<AppUserWarehouse>();
    public DbSet<StocktakingSession> Sessions => Set<StocktakingSession>();
    public DbSet<InventoryPart> InventoryParts => Set<InventoryPart>();
    public DbSet<PartChangeLog> PartChangeLogs => Set<PartChangeLog>();
    public DbSet<ExpectedVin> ExpectedVins => Set<ExpectedVin>();
    public DbSet<ScannedVin> ScannedVins => Set<ScannedVin>();
    public DbSet<QuantityCorrectionRequest> QuantityCorrections => Set<QuantityCorrectionRequest>();
    public DbSet<RecountRequest> RecountRequests => Set<RecountRequest>();
    public DbSet<VinCorrectionRequest> VinCorrections => Set<VinCorrectionRequest>();
    public DbSet<AdminBranch> AdminBranches => Set<AdminBranch>();
    public DbSet<AdminWarehouse> AdminWarehouses => Set<AdminWarehouse>();
    public DbSet<NewPartRequest> NewPartRequests => Set<NewPartRequest>();
    public DbSet<WorkflowAudit> WorkflowAudits => Set<WorkflowAudit>();
    public DbSet<DeviceRegistration> DeviceRegistrations => Set<DeviceRegistration>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Branch>(e => { e.Property(x => x.Name).HasMaxLength(150).IsRequired(); e.HasIndex(x => x.Name).IsUnique(); e.HasIndex(x => x.Number).IsUnique(); });
        b.Entity<Warehouse>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasOne(x => x.Branch).WithMany(x => x.Warehouses).HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.BranchId, x.Name }).IsUnique();
            e.HasIndex(x => x.Number).IsUnique();
        });
        b.Entity<Admin>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(11).IsRequired();
            e.HasIndex(x => x.PhoneNumber).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DeviceRegistration>(e =>
        {
            e.Property(x => x.Token).HasMaxLength(512).IsRequired();
            e.Property(x => x.Platform).HasMaxLength(32).IsRequired();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.AppUser).WithMany().HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AdminBranch>(e =>
        {
            e.HasKey(x => new { x.AdminId, x.BranchId });
            e.HasOne(x => x.Admin).WithMany(x => x.Branches).HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdminWarehouse>(e =>
        {
            e.HasKey(x => new { x.AdminId, x.WarehouseId });
            e.HasOne(x => x.Admin).WithMany(x => x.Warehouses).HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.PhoneNumber).HasMaxLength(11).IsRequired();
            e.HasIndex(x => x.PhoneNumber).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.HasOne(x => x.CreatedByAdmin).WithMany().HasForeignKey(x => x.CreatedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AppUserBranch>(e =>
        {
            e.HasKey(x => new { x.AppUserId, x.BranchId });
            e.HasOne(x => x.AppUser).WithMany(x => x.Branches).HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AppUserWarehouse>(e =>
        {
            e.HasKey(x => new { x.AppUserId, x.WarehouseId });
            e.HasOne(x => x.AppUser).WithMany(x => x.Warehouses).HasForeignKey(x => x.AppUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StocktakingSession>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.StoredFileName).HasMaxLength(260);
            e.HasOne(x => x.StartedByAdmin).WithMany().HasForeignKey(x => x.StartedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<InventoryPart>(e =>
        {
            e.Property(x => x.PartNumber).HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasMaxLength(400);
            e.Property(x => x.Price).HasPrecision(18, 2);
            e.Property(x => x.SystemLocation).HasMaxLength(64);
            e.Property(x => x.CountedLocation).HasMaxLength(64);
            e.Ignore(x => x.Difference); e.Ignore(x => x.IsMatched);
            e.HasIndex(x => new { x.SessionId, x.PartNumber });
            e.HasOne(x => x.Session).WithMany(x => x.Parts).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CountedByUser).WithMany().HasForeignKey(x => x.CountedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PartChangeLog>(e =>
        {
            e.Property(x => x.Field).HasMaxLength(64); e.Property(x => x.OldValue).HasMaxLength(200);
            e.Property(x => x.NewValue).HasMaxLength(200); e.Property(x => x.ChangedBy).HasMaxLength(150); e.Property(x => x.Source).HasMaxLength(64);
            e.HasOne(x => x.InventoryPart).WithMany(x => x.ChangeLogs).HasForeignKey(x => x.InventoryPartId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<ExpectedVin>(e =>
        {
            e.Property(x => x.Vin).HasMaxLength(17).IsRequired(); e.Property(x => x.Model).HasMaxLength(100); e.Property(x => x.Dealer).HasMaxLength(100);
            e.HasIndex(x => new { x.SessionId, x.Vin });
            e.HasOne(x => x.Session).WithMany(x => x.ExpectedVins).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ScannedVin>(e =>
        {
            e.Property(x => x.Vin).HasMaxLength(17).IsRequired();
            e.HasIndex(x => new { x.SessionId, x.Vin });
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<QuantityCorrectionRequest>(e =>
        {
            e.Property(x => x.PartNumber).HasMaxLength(64).IsRequired(); e.Property(x => x.PartDescription).HasMaxLength(400).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.InventoryPart).WithMany().HasForeignKey(x => x.InventoryPartId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.DecisionReason).HasMaxLength(1000);
            e.HasOne(x => x.RequestedByAdmin).WithMany().HasForeignKey(x => x.RequestedByAdminId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AudienceUser).WithMany().HasForeignKey(x => x.AudienceUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.Status, x.BranchId });
            e.HasIndex(x => new { x.Status, x.WarehouseId });
        });
        b.Entity<NewPartRequest>(e =>
        {
            e.Property(x => x.PartNumber).HasMaxLength(64).IsRequired();
            e.Property(x => x.NormalizedPartNumber).HasMaxLength(64).IsRequired();
            e.Property(x => x.Description).HasMaxLength(400).IsRequired();
            e.Property(x => x.Location).HasMaxLength(64).IsRequired();
            e.Property(x => x.ReturnReason).HasMaxLength(1000);
            e.HasOne(x => x.Warehouse).WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InventoryPart).WithMany().HasForeignKey(x => x.InventoryPartId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RequestedByUserId, x.CreatedAt });
            e.HasIndex(x => new { x.Status, x.BranchId, x.CreatedAt });
            e.HasIndex(x => new { x.SessionId, x.Status });
            var sqlite = Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
            e.HasIndex(x => new { x.SessionId, x.WarehouseId, x.NormalizedPartNumber }).IsUnique()
                .HasFilter(sqlite ? "\"Status\" IN (0, 1, 2)" : "[Status] IN (0, 1, 2)");
        });
        b.Entity<WorkflowAudit>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(32).IsRequired();
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(64);
            e.Property(x => x.ToStatus).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasIndex(x => new { x.Subject, x.SubjectId, x.CreatedAt });
            e.HasOne(x => x.ActorAdmin).WithMany().HasForeignKey(x => x.ActorAdminId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.NewPartRequest).WithMany(x => x.Audits).HasForeignKey(x => x.NewPartRequestId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RecountRequest>(e =>
        {
            e.HasOne(x => x.InventoryPart).WithMany().HasForeignKey(x => x.InventoryPartId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedUser).WithMany().HasForeignKey(x => x.AssignedUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedByAdmin).WithMany().HasForeignKey(x => x.RequestedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<VinCorrectionRequest>(e =>
        {
            e.Property(x => x.CurrentVin).HasMaxLength(17).IsRequired(); e.Property(x => x.NewVin).HasMaxLength(17).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.ScannedVin).WithMany().HasForeignKey(x => x.ScannedVinId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestedByUser).WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedByAdmin).WithMany().HasForeignKey(x => x.ApprovedByAdminId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
