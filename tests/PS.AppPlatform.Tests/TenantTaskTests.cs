using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.AppPlatform.Data;
using PS.AppPlatform.Endpoints;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;
using Xunit;

namespace PS.AppPlatform.Tests;

/// <summary>
/// Cross-cutting tenant isolation checks for background tasks (MT-05) that don't belong to
/// either BackgroundTaskServiceTests or TaskExecutionManagerTests specifically.
/// </summary>
public class TenantTaskTests
{
    private static readonly Guid TenantA = Guid.Parse("33333333-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("33333333-0000-0000-0000-000000000002");

    private class TestDbContext : PlatformDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }
    }

    private class TestScopedFactory : IScopedDbContextFactory<TestDbContext>
    {
        private readonly DbContextOptions<TestDbContext> _options;
        private readonly PlatformAssemblies _assemblies;
        private readonly Guid? _tenantId;

        public TestScopedFactory(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies, Guid? tenantId)
        {
            _options = options;
            _assemblies = assemblies;
            _tenantId = tenantId;
        }

        public TestDbContext CreateDbContext()
        {
            var context = new TestDbContext(_options, _assemblies);
            context.ApplyTenantScope(_tenantId is Guid tenantId ? TenantScope.For(tenantId) : TenantScope.Disabled);
            return context;
        }

        public TestDbContext CreateForTenant(Guid tenantId)
        {
            var context = new TestDbContext(_options, _assemblies);
            context.ApplyTenantScope(TenantScope.For(tenantId));
            return context;
        }

        public Task<TestDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    [Fact]
    public async Task GetNotificationsAsync_ScopedToTenant_ExcludesOtherTenantsTask()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var assemblies = new PlatformAssemblies().AddContaining<TenantTaskTests>();

        using (var seed = new TestDbContext(options, assemblies))
        {
            seed.ApplyTenantScope(TenantScope.Disabled);
            seed.BackgroundTasks.AddRange(
                new BackgroundTask
                {
                    Id = Guid.NewGuid(),
                    TaskType = "Test",
                    Status = BackgroundTaskStatus.Running,
                    RequiresNotification = true,
                    TenantId = TenantA
                },
                new BackgroundTask
                {
                    Id = Guid.NewGuid(),
                    TaskType = "Test",
                    Status = BackgroundTaskStatus.Running,
                    RequiresNotification = true,
                    TenantId = TenantB
                });
            await seed.SaveChangesAsync();
        }

        var scopedFactoryForA = new TestScopedFactory(options, assemblies, TenantA);
        var dbContextFactory = new PlatformDbContextFactoryAdapter<TestDbContext>(scopedFactoryForA);

        var endpoints = new BackgroundTaskEndpoints(
            Substitute.For<IBackgroundTaskService>(),
            Substitute.For<ITaskExecutionManager>(),
            Substitute.For<ITaskHandlerRegistry>(),
            dbContextFactory,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            Substitute.For<ILogger<BackgroundTaskEndpoints>>());

        var httpContext = new DefaultHttpContext();
        var result = await endpoints.GetNotificationsAsync(httpContext.Request);

        var okResult = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result);
        var tasks = Assert.IsAssignableFrom<System.Collections.IEnumerable>(okResult.Value).Cast<BackgroundTaskResponse>().ToList();

        Assert.Single(tasks);
    }

    [Fact]
    public async Task GetAllTasksAsync_NoneMode_ReturnsEveryTask()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        var assemblies = new PlatformAssemblies().AddContaining<TenantTaskTests>();

        using (var seed = new TestDbContext(options, assemblies))
        {
            seed.ApplyTenantScope(TenantScope.Disabled);
            seed.BackgroundTasks.AddRange(
                new BackgroundTask { Id = Guid.NewGuid(), TaskType = "Test", Status = BackgroundTaskStatus.New },
                new BackgroundTask { Id = Guid.NewGuid(), TaskType = "Test", Status = BackgroundTaskStatus.New });
            await seed.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestScopedFactory(options, assemblies, tenantId: null),
            new UnscopedDbContextFactory<TestDbContext>(provider),
            provider,
            config,
            new NullLogger());

        var tasks = await service.GetAllTasksAsync();

        Assert.Equal(2, tasks.Count);
    }

    private class NullLogger : ILogger<BackgroundTaskService<TestDbContext>>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}
