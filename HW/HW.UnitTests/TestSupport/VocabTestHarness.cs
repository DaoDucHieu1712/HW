using HW.Domain.Entities;
using HW.Infrastructure;
using HW.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HW.UnitTests.TestSupport;

/// <summary>
/// One test's worth of database: the real <see cref="ApplicationDbContext"/> on the in-memory
/// provider, behind the real <see cref="EFRepository{TEntity}"/>.
///
/// <para>
/// The vocab handlers lean on three things the context owns — the soft-delete query filter, the
/// <c>Word</c> value converter, and EF's async operators (<c>ToListAsync</c>, <c>CountAsync</c>).
/// A hand-rolled fake repository would have to reimplement all three and would then be the thing
/// actually under test, so the real pair is used and only the provider is swapped. Each harness
/// gets its own database name, so tests stay isolated even when run in parallel.
/// </para>
/// </summary>
public sealed class VocabTestHarness : IDisposable
{
    public VocabTestHarness()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"vocab-tests-{Guid.NewGuid()}")
            .EnableSensitiveDataLogging()
            .Options;

        Db = new ApplicationDbContext(options);
        Repository = new EFRepository<Vocab>(Db);
    }

    public ApplicationDbContext Db { get; }

    public EFRepository<Vocab> Repository { get; }

    /// <summary>
    /// Writes the given words and detaches them, so a handler reading one back gets its own tracked
    /// instance rather than the object the test is holding — the same as a fresh request would.
    /// </summary>
    public async Task<VocabTestHarness> SeedAsync(params Vocab[] vocabs)
    {
        Db.Vocabs.AddRange(vocabs);
        await Db.SaveChangesAsync();
        Db.ChangeTracker.Clear();

        foreach (var vocab in vocabs)
            vocab.ClearDomainEvents();

        return this;
    }

    /// <summary>Commits whatever a handler staged, standing in for the unit of work.</summary>
    public Task SaveAsync() => Db.SaveChangesAsync();

    /// <summary>Reads a word back through the query filter — null once it has been soft-deleted.</summary>
    public Task<Vocab?> ReloadAsync(string id) =>
        Db.Vocabs.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);

    public Task<Vocab?> ReloadIgnoringFiltersAsync(string id) =>
        Db.Vocabs.AsNoTracking().IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == id);

    public void Dispose() => Db.Dispose();
}
