using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VaultApp.Models;

namespace VaultApp.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<VaultEntry>   VaultEntries  { get; set; }
    public DbSet<SharedEntry>  SharedEntries { get; set; }
    public DbSet<PendingShare> PendingShares { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<VaultEntry>()
            .HasOne(v => v.Owner)
            .WithMany()
            .HasForeignKey(v => v.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SharedEntry>()
            .HasOne(s => s.VaultEntry)
            .WithMany(v => v.SharedWith)
            .HasForeignKey(s => s.VaultEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<SharedEntry>()
            .HasOne(s => s.SharedWithUser)
            .WithMany()
            .HasForeignKey(s => s.SharedWithUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PendingShare>()
            .HasOne(p => p.VaultEntry)
            .WithMany()
            .HasForeignKey(p => p.VaultEntryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<PendingShare>()
            .HasIndex(p => new { p.VaultEntryId, p.RecipientEmail })
            .IsUnique();
    }
}
