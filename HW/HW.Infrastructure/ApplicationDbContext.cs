using HW.Domain.Entities;
using HW.Domain.Entities.Outbox;
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

        builder.Entity<Vocab>(b =>
        {
            b.HasQueryFilter(x => x.IsDelete != true);
            b.Property(x => x.Word).IsRequired().HasMaxLength(200);
            b.Property(x => x.Meaning).HasMaxLength(500);
            b.Property(x => x.Example).HasMaxLength(1000);
            b.Property(x => x.Note).HasMaxLength(500);
        });
    }
}
