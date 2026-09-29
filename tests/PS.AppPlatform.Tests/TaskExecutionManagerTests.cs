using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class TaskExecutionManagerTests
{
    public class TestDbContext : PlatformDbContext
    {
        public TestDbContext(DbContextOptions<TestDbContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }
    }

    public class TenantAwareTestContext : PlatformDbContext
    {
        public TenantAwareTestContext(DbContextOptions<TenantAwareTestContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TenantNote>();
        }
    }

    public class TenantNote : TenantEntity
    {
        public string Content { get; set; } = "";
    }

    /// <summary>Wires up a PlatformDbContext-derived type against an InMemory database the
    /// same way AddPlatformData does against SQL Server, so tests exercise the real scoped
    /// and unscoped factories instead of mocking database access.</summary>
    private static IServiceCollection CreatePlatformServices<TContext>(
        string dbName, TenancyMode mode, PlatformAssemblies? assemblies = null)
        where TContext : PlatformDbContext
    {
        var services = new ServiceCollection();
        services.AddSingleton(assemblies ?? new PlatformAssemblies());
        services.AddSingleton(typeof(TenancyMode), mode);
        services.AddSingleton<TenantSaveChangesInterceptor>();

        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseInMemoryDatabase(dbName);
            options.AddInterceptors(sp.GetRequiredService<TenantSaveChangesInterceptor>());
        }, ServiceLifetime.Scoped, ServiceLifetime.Singleton);

        services.AddScoped<IScopedDbContextFactory<TContext>, ScopedDbContextFactory<TContext>>();
        services.AddScoped<IDbContextFactory<TContext>>(sp => sp.GetRequiredService<IScopedDbContextFactory<TContext>>());
        services.AddScoped<IUnscopedDbContextFactory<TContext>, UnscopedDbContextFactory<TContext>>();

        if (mode != TenancyMode.None)
        {
            services.AddScoped<TenantContext>();
            services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        }

        return services;
    }

    private static IConfiguration EmptyConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    public class TestHandler : ITaskHandler<string>
    {
        public Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            context.CompleteAsync().Wait();
            return Task.CompletedTask;
        }
    }

    public class FailingHandler : ITaskHandler<string>
    {
        public Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Handler failed");
        }
    }

    public class NeverCompletingHandler : ITaskHandler<string>
    {
        public Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    public class EndWithoutCompletingHandler : ITaskHandler<string>
    {
        public async Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            await context.EndWithoutCompletingAsync();
        }
    }

    public class ScopedDependency
    {
        public bool IsDisposed { get; set; }
    }

    public class ScopeDependencyTrackingHandler : ITaskHandler<string>
    {
        private readonly ScopedDependency _dependency;

        public ScopeDependencyTrackingHandler(ScopedDependency dependency)
        {
            _dependency = dependency;
        }

        public async Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            Assert.False(_dependency.IsDisposed, "Scoped dependency should not be disposed during handler execution");
            await context.CompleteAsync();
        }
    }

    /// <summary>Captures what a handler observed, so the test can assert on it after the
    /// manager's per-task scope (and everything resolved from it) has been disposed.</summary>
    public class ObservedNotes
    {
        public List<TenantNote> Notes { get; } = [];
    }

    public class TenantNoteHandler : ITaskHandler<string>
    {
        private readonly IDbContextFactory<TenantAwareTestContext> _factory;
        private readonly ObservedNotes _observed;

        public TenantNoteHandler(IDbContextFactory<TenantAwareTestContext> factory, ObservedNotes observed)
        {
            _factory = factory;
            _observed = observed;
        }

        public async Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            _observed.Notes.AddRange(await db.Set<TenantNote>().ToListAsync(cancellationToken));
            await context.CompleteAsync();
        }
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithRegisteredHandler_TransitionsToRunning()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("TestTask", typeof(TestHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<TestHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "TestTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test data\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Running);
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithUnknownTaskType_SetsFailed()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "UnknownTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "{}"
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(
            task.Id,
            BackgroundTaskStatus.Failed,
            Arg.Is<string>(msg => msg.Contains("UnknownTask"))
        );
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithThrowingHandler_SetsFailed()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("FailingTask", typeof(FailingHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<FailingHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "FailingTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(
            task.Id,
            BackgroundTaskStatus.Failed,
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithHandlerNotCompleting_SetsFailed()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("NeverCompleting", typeof(NeverCompletingHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<NeverCompletingHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "NeverCompleting",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(
            task.Id,
            BackgroundTaskStatus.Failed,
            "Task ended without completing"
        );
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithEndWithoutCompleting_LeavesPaused()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("EndWithoutCompleting", typeof(EndWithoutCompletingHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<EndWithoutCompletingHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "EndWithoutCompleting",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Paused);
        await taskService.DidNotReceive().UpdateStatusAsync(
            task.Id,
            BackgroundTaskStatus.Failed,
            Arg.Any<string>()
        );
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithCompleteHandler_SetsCompleted()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("TestTask", typeof(TestHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<TestHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "TestTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);
    }

    [Fact]
    public async Task ExecuteTaskAsync_HandlerResolvesFromOwnScope()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("ScopedTest", typeof(ScopeDependencyTrackingHandler));

        var services = CreatePlatformServices<TestDbContext>(Guid.NewGuid().ToString(), TenancyMode.None);
        services.AddScoped<ScopedDependency>();
        services.AddScoped<ScopeDependencyTrackingHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TestDbContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "ScopedTest",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);
    }

    [Fact]
    public async Task ExecuteTaskAsync_NoTenant_HandlerRunsWithUnresolvedTenantContext()
    {
        // A system task (no TenantId) must not silently see every tenant's data: the handler's
        // scope gets no SetSystem call, so a filtered query fails closed instead of leaking.
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("ReadNotes", typeof(TenantNoteHandler));

        var dbName = Guid.NewGuid().ToString();
        var services = CreatePlatformServices<TenantAwareTestContext>(dbName, TenancyMode.Multi);
        var observed = new ObservedNotes();
        services.AddSingleton(observed);
        services.AddScoped<TenantNoteHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TenantAwareTestContext>>();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "ReadNotes",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\"",
            TenantId = null
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TenantAwareTestContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TenantAwareTestContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(
            task.Id,
            BackgroundTaskStatus.Failed,
            Arg.Any<string>()
        );
        Assert.Empty(observed.Notes);
    }

    [Fact]
    public async Task ExecuteTaskAsync_RunsHandlerInTasksTenantScope()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("ReadNotes", typeof(TenantNoteHandler));

        var dbName = Guid.NewGuid().ToString();
        var services = CreatePlatformServices<TenantAwareTestContext>(dbName, TenancyMode.Multi);
        var observed = new ObservedNotes();
        services.AddSingleton(observed);
        services.AddScoped<TenantNoteHandler>();
        using var provider = services.BuildServiceProvider();
        var dbContextFactory = provider.GetRequiredService<IUnscopedDbContextFactory<TenantAwareTestContext>>();

        using (var seedContext = dbContextFactory.CreateDbContext())
        {
            seedContext.Set<TenantNote>().AddRange(
                new TenantNote { Id = Guid.NewGuid(), TenantId = tenantA, Content = "A's note" },
                new TenantNote { Id = Guid.NewGuid(), TenantId = tenantB, Content = "B's note" });
            await seedContext.SaveChangesAsync();
        }

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "ReadNotes",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\"",
            TenantId = tenantB
        };
        await SeedTaskAsync(dbContextFactory, task);

        var logger = Substitute.For<ILogger<TaskExecutionManager<TenantAwareTestContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TenantAwareTestContext>(
            taskService, registry, provider.GetRequiredService<IServiceScopeFactory>(), EmptyConfig(), logger, dbContextFactory, identity, gate);

        await manager.ExecuteTaskInternalAsync(task);

        var note = Assert.Single(observed.Notes);
        Assert.Equal(tenantB, note.TenantId);
        Assert.Equal("B's note", note.Content);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);
    }

    private static async Task SeedTaskAsync<TContext>(IUnscopedDbContextFactory<TContext> factory, BackgroundTask task)
        where TContext : PlatformDbContext
    {
        using var context = factory.CreateDbContext();
        context.BackgroundTasks.Add(task);
        await context.SaveChangesAsync();
    }
}
