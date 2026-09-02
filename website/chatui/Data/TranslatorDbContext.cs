using chatui.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace chatui.Data;

/// <summary>
/// EF Core DbContext for the translator workload. Uses snake_case naming via
/// EFCore.NamingConventions and enables the pgvector extension via
/// Pgvector.EntityFrameworkCore. Every repository query MUST scope by the
/// current userId — this class does not enforce that (no row-level security
/// in MVP).
/// </summary>
public class TranslatorDbContext(DbContextOptions<TranslatorDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.HasPostgresExtension("vector");

        builder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(u => u.EntraObjectId).IsRequired();
            e.HasIndex(u => u.EntraObjectId).IsUnique();
            e.Property(u => u.DisplayName).IsRequired().HasMaxLength(256);
            e.Property(u => u.Email).IsRequired().HasMaxLength(320);
            e.Property(u => u.PreferredLanguage).IsRequired().HasMaxLength(16).HasDefaultValue("en");
            e.Property(u => u.CreatedAt).HasDefaultValueSql("now()");
            e.Property(u => u.UpdatedAt).HasDefaultValueSql("now()");
        });
    }
}
