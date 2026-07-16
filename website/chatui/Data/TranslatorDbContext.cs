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

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Meeting> Meetings => Set<Meeting>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Turn> Turns => Set<Turn>();

    public DbSet<TurnTranslation> TurnTranslations => Set<TurnTranslation>();

    public DbSet<MeetingSummary> MeetingSummaries => Set<MeetingSummary>();

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

        builder.Entity<Conversation>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(c => new { c.UserAId, c.UserBId }).IsUnique();
            e.HasIndex(c => c.UserAId);
            e.HasIndex(c => c.UserBId);
            e.ToTable(t => t.HasCheckConstraint("ck_conversations_ordered", "user_a_id < user_b_id"));
            e.HasOne(c => c.UserA).WithMany().HasForeignKey(c => c.UserAId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(c => c.UserB).WithMany().HasForeignKey(c => c.UserBId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Message>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.SourceLang).IsRequired().HasMaxLength(16);
            e.Property(m => m.TargetLang).IsRequired().HasMaxLength(16);
            e.Property(m => m.Original).IsRequired();
            e.Property(m => m.Translated).IsRequired();
            e.Property(m => m.Embedding).HasColumnType("vector(1536)");
            e.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(m => new { m.ConversationId, m.CreatedAt }).IsDescending(false, true);
            e.HasOne(m => m.Conversation).WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.FromUser).WithMany().HasForeignKey(m => m.FromUserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Meeting>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(m => m.Status).IsRequired().HasMaxLength(16).HasDefaultValue("active");
            e.Property(m => m.Title).HasMaxLength(256);
            e.Property(m => m.CreatedAt).HasDefaultValueSql("now()");
            e.ToTable(t => t.HasCheckConstraint("ck_meetings_status", "status IN ('active','ended')"));
            e.HasIndex(m => new { m.InitiatorId, m.CreatedAt }).IsDescending(false, true);
            e.HasOne(m => m.Initiator).WithMany().HasForeignKey(m => m.InitiatorId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Participant>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(p => p.Language).IsRequired().HasMaxLength(16);
            e.Property(p => p.Role).IsRequired().HasMaxLength(16).HasDefaultValue("listener");
            e.Property(p => p.JoinedAt).HasDefaultValueSql("now()");
            e.ToTable(t => t.HasCheckConstraint("ck_participants_role", "role IN ('initiator','listener')"));
            e.HasIndex(p => new { p.MeetingId, p.UserId }).IsUnique();
            e.HasIndex(p => new { p.UserId, p.JoinedAt }).IsDescending(false, true);
            e.HasOne(p => p.Meeting).WithMany(m => m.Participants).HasForeignKey(p => p.MeetingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Turn>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(t => t.SourceLang).IsRequired().HasMaxLength(16);
            e.Property(t => t.Original).IsRequired();
            e.Property(t => t.Embedding).HasColumnType("vector(1536)");
            e.Property(t => t.CreatedAt).HasDefaultValueSql("now()");
            e.HasIndex(t => new { t.MeetingId, t.CreatedAt }).IsDescending(false, true);
            e.HasOne(t => t.Meeting).WithMany(m => m.Turns).HasForeignKey(t => t.MeetingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.Speaker).WithMany().HasForeignKey(t => t.SpeakerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(t => t.Addressee).WithMany().HasForeignKey(t => t.AddresseeId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TurnTranslation>(e =>
        {
            e.HasKey(tt => new { tt.TurnId, tt.TargetLang });
            e.Property(tt => tt.TargetLang).IsRequired().HasMaxLength(16);
            e.Property(tt => tt.Translated).IsRequired();
            e.HasOne(tt => tt.Turn).WithMany(t => t.Translations).HasForeignKey(tt => tt.TurnId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<MeetingSummary>(e =>
        {
            e.HasKey(s => new { s.MeetingId, s.Language });
            e.Property(s => s.Language).IsRequired().HasMaxLength(16);
            e.Property(s => s.Summary).IsRequired();
            e.Property(s => s.GeneratedAt).HasDefaultValueSql("now()");
            e.HasOne(s => s.Meeting).WithMany().HasForeignKey(s => s.MeetingId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
