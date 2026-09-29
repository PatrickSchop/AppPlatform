using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class BackgroundTaskServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("22222222-0000-0000-0000-000000000001");
    private static readonly Guid TenantB = Guid.Parse("22222222-0000-0000-0000-000000000002");

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
            // Mirrors ScopedDbContextFactory in TenancyMode.None: no tenant configured means
            // the filter is off entirely, not "enabled with an unresolved tenant".
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

    private class TestUnscopedFactory : IUnscopedDbContextFactory<TestDbContext>
    {
        private readonly DbContextOptions<TestDbContext> _options;
        private readonly PlatformAssemblies _assemblies;

        public TestUnscopedFactory(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
        {
            _options = options;
            _assemblies = assemblies;
        }

        public TestDbContext CreateDbContext()
        {
            var context = new TestDbContext(_options, _assemblies);
            context.ApplyTenantScope(TenantScope.Disabled);
            return context;
        }

        public Task<TestDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid? userId, Guid? tenantId)
        {
            UserId = userId;
            TenantId = tenantId;
        }

        public bool IsResolved => TenantId.HasValue;
        public Guid? UserId { get; }
        public Guid? TenantId { get; }
        public IReadOnlyList<Guid> TeamIds { get; } = [];
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
    }

    private class NullLogger : ILogger<BackgroundTaskService<TestDbContext>>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    private static BackgroundTaskService<TestDbContext> CreateService(
        DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies, Guid? userId = null, Guid? tenantId = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        if (tenantId is not null || userId is not null)
        {
            services.AddSingleton<ITenantContext>(new TestTenantContext(userId, tenantId));
        }
        var serviceProvider = services.BuildServiceProvider();

        return new BackgroundTaskService<TestDbContext>(
            new TestScopedFactory(options, assemblies, tenantId),
            new TestUnscopedFactory(options, assemblies),
            serviceProvider,
            config,
            new NullLogger()
        );
    }

    [Fact]
    public async Task CreateTaskAsync_PersistsRowWithNewStatusAndNullIds()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();
        var service = CreateService(options, assemblies);

        var taskId = await service.CreateTaskAsync("TestTask", "test data", "Test Description", false);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var task = await verifyContext.BackgroundTasks.FindAsync(taskId);

        Assert.NotNull(task);
        Assert.Equal(BackgroundTaskStatus.New, task.Status);
        Assert.Equal(0, task.CompletionPercentage);
        Assert.Null(task.ExecutionManagerId);
        Assert.Null(task.LeaseExpiresUtc);
        Assert.Null(task.TenantId);
        Assert.Null(task.CreatedByUserId);
    }

    [Fact]
    public async Task CreateTaskAsync_WithResolvedTenant_StampsTenantAndUser()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();
        var userId = Guid.NewGuid();
        var service = CreateService(options, assemblies, userId, TenantA);

        var taskId = await service.CreateTaskAsync("TestTask", "test data", "Test Description", false);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var task = await verifyContext.BackgroundTasks.FindAsync(taskId);

        Assert.NotNull(task);
        Assert.Equal(TenantA, task.TenantId);
        Assert.Equal(userId, task.CreatedByUserId);
    }

    [Fact]
    public async Task GetAllTasksAsync_ScopedToTenant_ExcludesOtherTenants()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        var serviceA = CreateService(options, assemblies, Guid.NewGuid(), TenantA);
        var serviceB = CreateService(options, assemblies, Guid.NewGuid(), TenantB);

        await serviceA.CreateTaskAsync("TestTask", "a", "A's task", false);
        await serviceB.CreateTaskAsync("TestTask", "b", "B's task", false);

        var tasksForA = await serviceA.GetAllTasksAsync();

        var task = Assert.Single(tasksForA);
        Assert.Equal("A's task", task.Description);
    }

    [Fact]
    public async Task GetTaskStatusAsync_ForOtherTenant_ReturnsNull()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        var serviceB = CreateService(options, assemblies, Guid.NewGuid(), TenantB);
        var taskId = await serviceB.CreateTaskAsync("TestTask", "b", "B's task", false);

        var serviceA = CreateService(options, assemblies, Guid.NewGuid(), TenantA);
        var result = await serviceA.GetTaskStatusAsync(taskId);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateStatusAsync_Completed_ClearsExecutionManagerIdAndLeaseExpiresUtc()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        using var context = new TestDbContext(options, assemblies);
        context.ApplyTenantScope(TenantScope.Disabled);
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.Running,
            ExecutionManagerId = Guid.NewGuid(),
            LeaseExpiresUtc = DateTime.UtcNow.AddSeconds(300)
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var service = CreateService(options, assemblies);

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Completed, updatedTask.Status);
        Assert.Null(updatedTask.ExecutionManagerId);
        Assert.Null(updatedTask.LeaseExpiresUtc);
        Assert.Equal(100, updatedTask.CompletionPercentage);
    }

    [Fact]
    public async Task UpdateStatusAsync_Running_DoesNotClearExecutionManagerId()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        using var context = new TestDbContext(options, assemblies);
        context.ApplyTenantScope(TenantScope.Disabled);
        var managerId = Guid.NewGuid();
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.NotStarted,
            ExecutionManagerId = managerId
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var service = CreateService(options, assemblies);

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Running);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Running, updatedTask.Status);
        Assert.Equal(managerId, updatedTask.ExecutionManagerId);
    }

    [Fact]
    public async Task UpdateStatusAsync_Failed_SetMessageAndCompletedDate()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        using var context = new TestDbContext(options, assemblies);
        context.ApplyTenantScope(TenantScope.Disabled);
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.Running,
            ExecutionManagerId = Guid.NewGuid()
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var service = CreateService(options, assemblies);

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Failed, "boom");

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Failed, updatedTask.Status);
        Assert.Equal("boom", updatedTask.StatusMessage);
        Assert.NotNull(updatedTask.CompletedDate);
    }

    [Fact]
    public async Task ResumeTaskAsync_FromPaused_ResetsAndReturnsTrue()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        using var context = new TestDbContext(options, assemblies);
        context.ApplyTenantScope(TenantScope.Disabled);
        var now = DateTime.UtcNow;
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.Paused,
            ExecutionManagerId = Guid.NewGuid(),
            StartedDate = now,
            CompletedDate = now
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var service = CreateService(options, assemblies);

        var result = await service.ResumeTaskAsync(task.Id);

        Assert.True(result);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Resumed, updatedTask.Status);
        Assert.Null(updatedTask.ExecutionManagerId);
        Assert.Null(updatedTask.StartedDate);
        Assert.Null(updatedTask.CompletedDate);
    }

    [Fact]
    public async Task ResumeTaskAsync_FromRunning_ReturnsFalseAndChangesNothing()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        using var context = new TestDbContext(options, assemblies);
        context.ApplyTenantScope(TenantScope.Disabled);
        var managerId = Guid.NewGuid();
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.Running,
            ExecutionManagerId = managerId
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var service = CreateService(options, assemblies);

        var result = await service.ResumeTaskAsync(task.Id);

        Assert.False(result);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Running, updatedTask.Status);
        Assert.Equal(managerId, updatedTask.ExecutionManagerId);
    }

    [Fact]
    public async Task ResumeTaskAsync_ForOtherTenantsPausedTask_ReturnsFalseAndLeavesRowUnchanged()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        var serviceB = CreateService(options, assemblies, Guid.NewGuid(), TenantB);
        var taskId = await serviceB.CreateTaskAsync("TestTask", "b", "B's task", false);
        await serviceB.UpdateStatusAsync(taskId, BackgroundTaskStatus.Paused);

        var serviceA = CreateService(options, assemblies, Guid.NewGuid(), TenantA);
        var result = await serviceA.ResumeTaskAsync(taskId);

        Assert.False(result);

        using var verifyContext = new TestDbContext(options, assemblies);
        verifyContext.ApplyTenantScope(TenantScope.Disabled);
        var row = await verifyContext.BackgroundTasks.FindAsync(taskId);
        Assert.NotNull(row);
        Assert.Equal(BackgroundTaskStatus.Paused, row.Status);
    }

    [Fact]
    public void ConstructService_WithoutApiBaseUrl_DoesNotThrow()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        var service = CreateService(options, assemblies);

        Assert.NotNull(service);
    }

    [Fact]
    public void ExecutionManagerIdentity_is_stable_across_scopes()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ExecutionManagerIdentity>();

        var provider = services.BuildServiceProvider();

        var scope1 = provider.CreateScope();
        var id1 = scope1.ServiceProvider.GetRequiredService<ExecutionManagerIdentity>();

        var scope2 = provider.CreateScope();
        var id2 = scope2.ServiceProvider.GetRequiredService<ExecutionManagerIdentity>();

        var id3 = provider.GetRequiredService<ExecutionManagerIdentity>();

        Assert.Same(id1, id2);
        Assert.Same(id2, id3);
        Assert.Equal(id1.Id, id2.Id);
        Assert.Equal(id2.Id, id3.Id);
    }
}
