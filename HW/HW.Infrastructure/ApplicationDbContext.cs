using HW.Domain.Entities;
using HW.Domain.Entities.Outbox;
using HW.Domain.Entities.Sagas;
using HW.Domain.Enums;
using HW.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace HW.Infrastructure;

public class ApplicationDbContext : IdentityDbContext<AppUser>
{
    public ApplicationDbContext() { }

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public virtual DbSet<AppUser> AppUsers { get; set; }
    public virtual DbSet<MasterData> MasterDatas { get; set; }
    public virtual DbSet<Blog> Blogs { get; set; }
    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }
    public virtual DbSet<Vocab> Vocabs { get; set; }
    public virtual DbSet<UserFitnessProfile> UserFitnessProfiles { get; set; }
    public virtual DbSet<Folder> Folders { get; set; }
    public virtual DbSet<Note> Notes { get; set; }

    /// <summary>The saga event store — append-only, and the only source of truth for saga state.</summary>
    public virtual DbSet<SagaEventRecord> SagaEvents { get; set; }

    /// <summary>Disposable projection over <see cref="SagaEvents"/>, for querying and monitoring.</summary>
    public virtual DbSet<SagaInstance> SagaInstances { get; set; }

    /// <summary>Consumed-message claims that make at-least-once delivery safe for a saga.</summary>
    public virtual DbSet<SagaInboxEntry> SagaInbox { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Blog>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.Property(x => x.Title)
                .HasConversion(t => t.Value, v => BlogTitle.FromPersistence(v))
                .HasMaxLength(BlogTitle.MaxLength);
            b.Property(x => x.Content)
                .HasConversion(c => c.Value, v => BlogContent.FromPersistence(v));
        });

        builder.Entity<OutboxMessage>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired().HasMaxLength(500);
            b.Property(x => x.Content).IsRequired();
        });

        builder.Entity<SagaEventRecord>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.SagaId).IsRequired().HasMaxLength(100);
            b.Property(x => x.SagaType).IsRequired().HasMaxLength(200);
            b.Property(x => x.Type).IsRequired().HasMaxLength(500);
            b.Property(x => x.Content).IsRequired().HasColumnType("longtext");
            b.Property(x => x.CausationMessageId).HasMaxLength(100);

            // The optimistic-concurrency check, not merely an index. Two replies racing to advance
            // the same saga both compute the same next version; this is what makes the loser's
            // transaction fail so it can be retried against the winner's state, instead of two
            // conflicting decisions both being written.
            b.HasIndex(x => new { x.SagaId, x.Version }).IsUnique();
        });

        builder.Entity<SagaInstance>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).HasMaxLength(100);
            b.Property(x => x.SagaType).IsRequired().HasMaxLength(200);
            b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.CurrentStep).IsRequired().HasMaxLength(100);
            b.Property(x => x.FailureReason).HasMaxLength(1000);

            // Supports the query this table exists for: find every saga still in flight.
            b.HasIndex(x => new { x.SagaType, x.Status });
        });

        builder.Entity<SagaInboxEntry>(b =>
        {
            // MessageId is the key outright: the uniqueness of the broker's message id is the
            // deduplication guarantee, so it must be enforced by the database rather than by a
            // check the application performs before it commits.
            b.HasKey(x => x.MessageId);
            b.Property(x => x.MessageId).HasMaxLength(100);
            b.Property(x => x.SagaId).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.SagaId);
        });

        builder.Entity<Vocab>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.Property(x => x.Word)
                .HasConversion(w => w.Value, v => Word.FromPersistence(v))
                .IsRequired()
                .HasMaxLength(Word.MaxLength);
            b.Property(x => x.Content).HasColumnType("longtext");
        });

        builder.Entity<Folder>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.HasOne(f => f.Parent)
                .WithMany(f => f.Children)
                .HasForeignKey(f => f.ParentId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Note>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.Property(x => x.Title).HasMaxLength(500);
            b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            b.HasOne(n => n.Folder)
                .WithMany(f => f.Notes)
                .HasForeignKey(n => n.FolderId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<UserFitnessProfile>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.OwnsOne(x => x.PersonalInfo, pi =>
            {
                pi.Property(x => x.Age).HasColumnName("Age").IsRequired();
                pi.Property(x => x.Gender).HasConversion<string>().HasColumnName("Gender").HasMaxLength(20);
                pi.Property(x => x.HeightCm).HasColumnName("HeightCm").HasPrecision(5, 2);
                pi.Property(x => x.WeightKg).HasColumnName("WeightKg").HasPrecision(5, 2);
            });
            b.Property(x => x.ActivityLevel).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.FitnessLevel).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.Goal).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.DietaryPreference).HasConversion<string>().HasMaxLength(20);
            b.Property(x => x.AvailableEquipment)
                .HasConversion(
                    v => Newtonsoft.Json.JsonConvert.SerializeObject(v),
                    v => Newtonsoft.Json.JsonConvert.DeserializeObject<List<Equipment>>(v) ?? new())
                .HasColumnType("longtext");
            b.Property(x => x.Allergies)
                .HasConversion(
                    v => Newtonsoft.Json.JsonConvert.SerializeObject(v),
                    v => Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(v) ?? new())
                .HasColumnType("longtext");
            b.Property(x => x.InjuriesOrLimitations)
                .HasConversion(
                    v => Newtonsoft.Json.JsonConvert.SerializeObject(v),
                    v => Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(v) ?? new())
                .HasColumnType("longtext");
        });
    }
}
