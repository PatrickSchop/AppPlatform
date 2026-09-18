using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;
using Xunit;

namespace PS.AppPlatform.Tests;

public class BackgroundTaskServiceTests
{
    private class TestDbContext : PlatformDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }
    }

    private class TestDbContextFactory : IDbContextFactory<TestDbContext>
    {
        private readonly DbContextOptions<TestDbContext> _options;
        private readonly PlatformAssemblies _assemblies;

        public TestDbContextFactory(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
        {
            _options = options;
            _assemblies = assemblies;
        }

        public TestDbContext CreateDbContext() => new(_options, _assemblies);
        public Task<TestDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private class NullLogger : ILogger<BackgroundTaskService<TestDbContext>>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }

    [Fact]
    public async Task CreateTaskAsync_PersistsRowWithNewStatusAndNullIds()
    {
        var dbName = Guid.NewGuid().ToString();
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        var taskId = await service.CreateTaskAsync("TestTask", "test data", "Test Description", false);

        using var verifyContext = new TestDbContext(options, assemblies);
        var task = await verifyContext.BackgroundTasks.FindAsync(taskId);

        Assert.NotNull(task);
        Assert.Equal(BackgroundTaskStatus.New, task.Status);
        Assert.Equal(0, task.CompletionPercentage);
        Assert.Null(task.ExecutionManagerId);
        Assert.Null(task.LeaseExpiresUtc);
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);

        using var verifyContext = new TestDbContext(options, assemblies);
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Running);

        using var verifyContext = new TestDbContext(options, assemblies);
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
        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "Test",
            Status = BackgroundTaskStatus.Running,
            ExecutionManagerId = Guid.NewGuid()
        };
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        await service.UpdateStatusAsync(task.Id, BackgroundTaskStatus.Failed, "boom");

        using var verifyContext = new TestDbContext(options, assemblies);
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        var result = await service.ResumeTaskAsync(task.Id);

        Assert.True(result);

        using var verifyContext = new TestDbContext(options, assemblies);
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

        var result = await service.ResumeTaskAsync(task.Id);

        Assert.False(result);

        using var verifyContext = new TestDbContext(options, assemblies);
        var updatedTask = await verifyContext.BackgroundTasks.FindAsync(task.Id);

        Assert.NotNull(updatedTask);
        Assert.Equal(BackgroundTaskStatus.Running, updatedTask.Status);
        Assert.Equal(managerId, updatedTask.ExecutionManagerId);
    }

    [Fact]
    public void ConstructService_WithoutApiBaseUrl_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var assemblies = new PlatformAssemblies().AddContaining<BackgroundTaskServiceTests>();

        var service = new BackgroundTaskService<TestDbContext>(
            new TestDbContextFactory(options, assemblies),
            config,
            new MockHttpClientFactory(),
            new NullLogger()
        );

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

    private class MockHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

