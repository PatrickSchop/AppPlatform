using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class TenantDataTests
{
    private static readonly Guid TenantA = Guid.Parse("11111111-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("11111111-0000-0000-0000-000000000002");

    [Fact]
    public async Task ScopedContext_FiltersTenantA()
    {
        using var context = CreateContextForTenant(TenantA);
        var note = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantA, Content = "Test A" };
        context.Set<TenantNote>().Add(note);
        await context.SaveChangesAsync();

        var notes = context.Set<TenantNote>().ToList();

        Assert.Single(notes);
        Assert.Equal(TenantA, notes[0].TenantId);
    }

    [Fact]
    public async Task UnscopedFactory_ReturnsAllTenants()
    {
        var dbName = Guid.NewGuid().ToString();

        using (var contextA = CreateContextWithDbName(TenantA, dbName))
        {
            var noteA = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantA, Content = "Test A" };
            contextA.Set<TenantNote>().Add(noteA);
            await contextA.SaveChangesAsync();
        }

        using (var contextB = CreateContextWithDbName(TenantB, dbName))
        {
            var noteB = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantB, Content = "Test B" };
            contextB.Set<TenantNote>().Add(noteB);
            await contextB.SaveChangesAsync();
        }

        using (var unscopedContext = CreateUnscopedContextWithDbName(dbName))
        {
            var notes = unscopedContext.Set<TenantNote>().ToList();
            Assert.Equal(2, notes.Count);
        }
    }

    [Fact]
    public void ScopedWithNoTenant_ThrowsOnQueryingTenantEntity()
    {
        using var context = CreateContextWithoutTenant();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            context.Set<TenantNote>().ToList());

        Assert.IsType<TenantContextMissingException>(exception);
    }

    [Fact]
    public void ScopedWithNoTenant_SucceedsOnQueryingApplicationEntity()
    {
        using var context = CreateContextWithoutTenant();

        var settings = context.Set<Setting>().ToList();

        Assert.Empty(settings);
    }

    [Fact]
    public async Task ScopedInsert_StampsUnsetTenantId()
    {
        using var context = CreateContextForTenant(TenantA);
        var note = new TenantNote { Id = Guid.NewGuid(), Content = "Test" };
        context.Set<TenantNote>().Add(note);

        await context.SaveChangesAsync();

        Assert.Equal(TenantA, note.TenantId);
    }

    [Fact]
    public async Task ScopedInsert_WithWrongTenant_Throws()
    {
        using var context = CreateContextForTenant(TenantA);
        var note = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantB, Content = "Test" };
        context.Set<TenantNote>().Add(note);

        var exception = await Assert.ThrowsAsync<TenantMismatchException>(() => context.SaveChangesAsync());
        Assert.Contains("different tenant", exception.Message);
    }

    [Fact]
    public async Task ScopedModify_FromDifferentTenant_Throws()
    {
        var dbName = Guid.NewGuid().ToString();

        using (var unscopedContext = CreateUnscopedContextWithDbName(dbName))
        {
            var note = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantB, Content = "Original" };
            unscopedContext.Set<TenantNote>().Add(note);
            await unscopedContext.SaveChangesAsync();
        }

        using (var scopedContextA = CreateContextWithDbName(TenantA, dbName))
        {
            var note = scopedContextA.Set<TenantNote>().First();
            note.Content = "Modified";

            var exception = await Assert.ThrowsAsync<TenantMismatchException>(() => scopedContextA.SaveChangesAsync());
            Assert.Contains("different tenant", exception.Message);
        }
    }

    [Fact]
    public async Task TwoContexts_SameTenantType_ProperIsolation()
    {
        var dbName = Guid.NewGuid().ToString();

        using (var contextA1 = CreateContextWithDbName(TenantA, dbName))
        {
            var note = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantA, Content = "Test A" };
            contextA1.Set<TenantNote>().Add(note);
            await contextA1.SaveChangesAsync();
        }

        using (var contextB = CreateContextWithDbName(TenantB, dbName))
        {
            var noteB = new TenantNote { Id = Guid.NewGuid(), TenantId = TenantB, Content = "Test B" };
            contextB.Set<TenantNote>().Add(noteB);
            await contextB.SaveChangesAsync();

            var notesB = contextB.Set<TenantNote>().ToList();
            Assert.Single(notesB);
            Assert.Equal(TenantB, notesB[0].TenantId);
        }

        using (var contextA2 = CreateContextWithDbName(TenantA, dbName))
        {
            var notesA = contextA2.Set<TenantNote>().ToList();
            Assert.Single(notesA);
            Assert.Equal(TenantA, notesA[0].TenantId);
        }
    }

    [Fact]
    public void NoneModeWithTenantEntity_Throws()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContextWithTenantEntity>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(new PlatformAssemblies());
        services.TryAdd(ServiceDescriptor.Singleton(typeof(TenancyMode), TenancyMode.None));

        using var provider = services.BuildServiceProvider();
        var factory = new ScopedDbContextFactory<TestContextWithTenantEntity>(provider, TenancyMode.None);

        Assert.Throws<InvalidOperationException>(() => factory.CreateDbContext());
    }

    [Fact]
    public void NoneModeWithoutTenantEntity_Succeeds()
    {
        var services = new ServiceCollection();
        services.AddDbContext<TestContextWithoutTenantEntity>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(new PlatformAssemblies());
        services.TryAdd(ServiceDescriptor.Singleton(typeof(TenancyMode), TenancyMode.None));

        using var provider = services.BuildServiceProvider();
        var factory = new ScopedDbContextFactory<TestContextWithoutTenantEntity>(provider, TenancyMode.None);

        using var context = factory.CreateDbContext();
        var settings = context.Set<Setting>().ToList();

        Assert.Empty(settings);
    }

    [Fact]
    public async Task UnscopedInsert_UnsetTenantId_Throws()
    {
        using var context = CreateUnscopedContext();
        var note = new TenantNote { Id = Guid.NewGuid(), Content = "Test" };
        context.Set<TenantNote>().Add(note);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains("must set TenantId", exception.Message);
    }

    [Fact]
    public void ApplyTenantScope_Twice_Throws()
    {
        using var context = CreateContextForTenant(TenantA);

        Assert.Throws<InvalidOperationException>(() =>
            context.ApplyTenantScope(TenantScope.For(TenantB)));
    }

    private TestDbContext CreateContextForTenant(Guid tenantId)
    {
        return CreateContextWithDbName(tenantId, Guid.NewGuid().ToString());
    }

    private TestDbContext CreateContextWithDbName(Guid tenantId, string dbName)
    {
        var context = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options, new PlatformAssemblies());
        context.ApplyTenantScope(TenantScope.For(tenantId));
        return context;
    }

    private TestDbContext CreateContextWithoutTenant()
    {
        var context = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options, new PlatformAssemblies());
        context.ApplyTenantScope(TenantScope.For(null));
        return context;
    }

    private TestDbContext CreateUnscopedContext()
    {
        return CreateUnscopedContextWithDbName(Guid.NewGuid().ToString());
    }

    private TestDbContext CreateUnscopedContextWithDbName(string dbName)
    {
        var context = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options, new PlatformAssemblies());
        context.ApplyTenantScope(TenantScope.Disabled);
        return context;
    }

    private class TestDbContext : PlatformDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantNote>();
            modelBuilder.Entity<Setting>();
        }
    }

    private class TestContextWithTenantEntity : PlatformDbContext
    {
        public TestContextWithTenantEntity(DbContextOptions<TestContextWithTenantEntity> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantNote>();
        }
    }

    private class TestContextWithoutTenantEntity : PlatformDbContext
    {
        public TestContextWithoutTenantEntity(DbContextOptions<TestContextWithoutTenantEntity> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Setting>();
        }
    }

    private class TenantNote : TenantEntity
    {
        public string Content { get; set; } = "";
    }

    private class Setting : Entity
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
