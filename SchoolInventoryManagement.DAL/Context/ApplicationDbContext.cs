using Microsoft.EntityFrameworkCore;
using SchoolInventoryManagement.DAL.Entities;

namespace SchoolInventoryManagement.DAL.Context
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Asset> Assets { get; set; } = null!;
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<Branch> Branches { get; set; } = null!;
        public DbSet<Department> Departments { get; set; } = null!;
        public DbSet<Location> Locations { get; set; } = null!;
        public DbSet<Category> Categories { get; set; } = null!;
        public DbSet<AssetRequest> AssetRequests { get; set; } = null!;
        public DbSet<AssetAssignment> AssetAssignments { get; set; } = null!;
        public DbSet<AssetMovement> AssetMovements { get; set; } = null!;
        public DbSet<DisposalRecord> DisposalRecords { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<Notification> Notifications { get; set; } = null!;
        public DbSet<Model> Models { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // =====================================================
            // BRANCH → Department / Location / User / Asset
            // (all inferred by EF from FK + nav properties, but
            //  DeleteBehavior set explicitly on every relationship
            //  per our "no cascading deletes" decision)
            // =====================================================
            modelBuilder.Entity<Department>()
                .HasOne(d => d.Branch)
                .WithMany(b => b.Departments)
                .HasForeignKey(d => d.BranchID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Location>()
                .HasOne(l => l.Branch)
                .WithMany(b => b.Locations)
                .HasForeignKey(l => l.BranchID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Branch)
                .WithMany(b => b.Users)
                .HasForeignKey(u => u.BranchID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Asset>()
                .HasOne(a => a.Branch)
                .WithMany(b => b.Assets)
                .HasForeignKey(a => a.BranchID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // DEPARTMENT → User / Asset / AssetRequest
            // =====================================================
            modelBuilder.Entity<User>()
                .HasOne(u => u.Department)
                .WithMany(d => d.Users)
                .HasForeignKey(u => u.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Asset>()
                .HasOne(a => a.Department)
                .WithMany(d => d.Assets)
                .HasForeignKey(a => a.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.Department)
                .WithMany(d => d.AssetRequests)
                .HasForeignKey(ar => ar.DepartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ROLE → User
            // =====================================================
            modelBuilder.Entity<User>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleID)
                .OnDelete(DeleteBehavior.Restrict);
            // =====================================================
            // CATEGORY → Model
            // =====================================================
            modelBuilder.Entity<Model>()
                .HasOne(m => m.Category)
                .WithMany(c => c.Models)
                .HasForeignKey(m => m.CategoryID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // MODEL → Asset / AssetRequest
            // =====================================================
            modelBuilder.Entity<Asset>()
                .HasOne(a => a.Model)
                .WithMany(m => m.Assets)
                .HasForeignKey(a => a.ModelID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.Model)
                .WithMany(m => m.AssetRequests)
                .HasForeignKey(ar => ar.ModelID)
                .OnDelete(DeleteBehavior.Restrict);
            // =====================================================
            // LOCATION → Asset (CurrentLocation) / AssetRequest
            // =====================================================
            modelBuilder.Entity<Asset>()
                .HasOne(a => a.CurrentLocation)
                .WithMany(l => l.Assets)
                .HasForeignKey(a => a.CurrentLocationID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.RequestedLocation)
                .WithMany(l => l.AssetRequests)
                .HasForeignKey(ar => ar.RequestedLocationID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ASSET → AssignedUser (User)
            // =====================================================
            modelBuilder.Entity<Asset>()
                .HasOne(a => a.AssignedUser)
                .WithMany(u => u.AssignedAssets)
                .HasForeignKey(a => a.AssignedUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ASSET ↔ DISPOSAL RECORD (one-to-one)
            // Must be explicit — EF won't infer 1:1 from a nullable
            // nav alone. UNIQUE(AssetID) in SQL is what makes this 1:1.
            // =====================================================
            modelBuilder.Entity<DisposalRecord>()
     .HasOne(dr => dr.Asset)
     .WithMany(a => a.DisposalRecords)
     .HasForeignKey(dr => dr.AssetID)
     .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DisposalRecord>()
                .HasOne(dr => dr.RestoredByUser)
                .WithMany()
                .HasForeignKey(dr => dr.RestoredByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<DisposalRecord>()
                .HasOne(dr => dr.ApprovedByUser)
                .WithMany(u => u.ApprovedDisposals)
                .HasForeignKey(dr => dr.ApprovedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ASSET REQUEST → Asset (nullable) / RequestedByUser / ApprovedByUser
            // Multiple FKs to User on this entity — must specify each explicitly
            // =====================================================
            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.Asset)
                .WithMany(a => a.AssetRequests)
                .HasForeignKey(ar => ar.AssetID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.RequestedByUser)
                .WithMany(u => u.MadeRequests)
                .HasForeignKey(ar => ar.RequestedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetRequest>()
                .HasOne(ar => ar.ApprovedByUser)
                .WithMany(u => u.ApprovedRequests)
                .HasForeignKey(ar => ar.ApprovedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ASSET ASSIGNMENT → Asset / AssignedToUser / AssignedByUser
            // Two FKs to User — must specify each explicitly
            // =====================================================
            modelBuilder.Entity<AssetAssignment>()
                .HasOne(aa => aa.Asset)
                .WithMany(a => a.AssetAssignments)
                .HasForeignKey(aa => aa.AssetID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetAssignment>()
                .HasOne(aa => aa.AssignedToUser)
                .WithMany(u => u.ReceivedAssignments)
                .HasForeignKey(aa => aa.AssignedToUserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetAssignment>()
                .HasOne(aa => aa.AssignedByUser)
                .WithMany(u => u.GivenAssignments)
                .HasForeignKey(aa => aa.AssignedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ASSET MOVEMENT → Asset / SourceLocation / DestinationLocation / MovedByUser
            // Two FKs to Location — must specify each explicitly
            // =====================================================
            modelBuilder.Entity<AssetMovement>()
                .HasOne(am => am.Asset)
                .WithMany(a => a.AssetMovements)
                .HasForeignKey(am => am.AssetID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetMovement>()
                .HasOne(am => am.SourceLocation)
                .WithMany(l => l.OutboundMovements)
                .HasForeignKey(am => am.SourceLocationID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetMovement>()
                .HasOne(am => am.DestinationLocation)
                .WithMany(l => l.InboundMovements)
                .HasForeignKey(am => am.DestinationLocationID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AssetMovement>()
                .HasOne(am => am.MovedByUser)
                .WithMany(u => u.ExecutedMovements)
                .HasForeignKey(am => am.MovedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // AUDIT LOG → User / TargetAsset (nullable)
            // =====================================================
            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(al => al.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.TargetAsset)
                .WithMany(a => a.AuditLogs)
                .HasForeignKey(al => al.TargetAssetID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // NOTIFICATION → User
            // =====================================================
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany() // no ICollection<Notification> nav on User — not needed
                .HasForeignKey(n => n.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            // =====================================================
            // ENUM → STRING CONVERSIONS
            // Keeps existing VARCHAR columns compatible with C# enums
            // =====================================================
            modelBuilder.Entity<Asset>()
                .Property(a => a.Condition)
                .HasConversion<string>()
                .HasMaxLength(30);

            modelBuilder.Entity<Asset>()
                .Property(a => a.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            modelBuilder.Entity<AssetRequest>()
                .Property(ar => ar.RequestType)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<AssetRequest>()
                .Property(ar => ar.RequestStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<AssetAssignment>()
                .Property(aa => aa.ConditionOnAssignment)
                .HasConversion<string>()
                .HasMaxLength(30);

            modelBuilder.Entity<AssetAssignment>()
                .Property(aa => aa.ConditionOnReturn)
                .HasConversion<string>()
                .HasMaxLength(30);

            modelBuilder.Entity<AssetMovement>()
                .Property(am => am.ConditionOnTransfer)
                .HasConversion<string>()
                .HasMaxLength(30);

            // =====================================================
            // UNIQUE CONSTRAINTS
            // =====================================================
            modelBuilder.Entity<Asset>()
                .HasIndex(a => a.AssetCode)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Category>()
                .HasIndex(c => c.CategoryName)
                .IsUnique();

            modelBuilder.Entity<Role>()
                .HasIndex(r => r.RoleName)
                .IsUnique();

            // =====================================================
            // CHECK CONSTRAINT: AssetRequests must have CategoryID
            // OR AssetID (mirrors CK_AssetRequests_AssetOrCategory in SQL)
            // =====================================================
            modelBuilder.Entity<AssetRequest>()
     .ToTable(t => t.HasCheckConstraint(
         "CK_AssetRequests_TypeFieldRules",
         "([RequestType] = 'Borrow' AND [ModelID] IS NOT NULL AND [AssetID] IS NULL AND [RequestedLocationID] IS NULL) " +
         "OR ([RequestType] = 'Transfer' AND [AssetID] IS NOT NULL AND [RequestedLocationID] IS NOT NULL AND [ModelID] IS NULL)"
     ));
            // =====================================================
            // DEFAULT VALUES: GETDATE() — server local time
            // =====================================================
            modelBuilder.Entity<AssetAssignment>()
                .Property(aa => aa.AssignmentDate)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<AssetMovement>()
                .Property(am => am.DateMoved)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<DisposalRecord>()
                .Property(dr => dr.DisposalDate)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<AuditLog>()
                .Property(al => al.LogDateTime)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<Notification>()
                .Property(n => n.CreatedAt)
                .HasDefaultValueSql("GETDATE()");

            modelBuilder.Entity<AssetRequest>()
                .Property(ar => ar.RequestDate)
                .HasDefaultValueSql("GETDATE()");
        }
    }
}