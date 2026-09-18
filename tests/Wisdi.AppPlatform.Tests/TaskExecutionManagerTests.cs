using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tasks;
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

    [Fact]
    public async Task ExecuteTaskAsync_WithRegisteredHandler_TransitionsToRunning()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("TestTask", typeof(TestHandler));

        var services = new ServiceCollection();
        services.AddScoped<TestHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "TestTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test data\""
        };

        // Set up mock to return task when asked, with Completed status after handler runs
        taskService.GetTaskStatusAsync(task.Id)
            .Returns(x =>
            {
                var completedTask = new BackgroundTask { Id = task.Id, Status = BackgroundTaskStatus.Completed };
                return Task.FromResult<BackgroundTask?>(completedTask);
            });

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

        await manager.ExecuteTaskInternalAsync(task);

        // Verify Running was called
        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Running);
    }

    [Fact]
    public async Task ExecuteTaskAsync_WithUnknownTaskType_SetsFailed()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();

        var services = new ServiceCollection();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "UnknownTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "{}"
        };

        taskService.GetTaskStatusAsync(task.Id)
            .Returns(Task.FromResult<BackgroundTask?>(task));

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

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

        var services = new ServiceCollection();
        services.AddScoped<FailingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "FailingTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

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

        var services = new ServiceCollection();
        services.AddScoped<NeverCompletingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "NeverCompleting",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };

        taskService.GetTaskStatusAsync(task.Id).Returns(Task.FromResult<BackgroundTask?>(task));

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

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

        var services = new ServiceCollection();
        services.AddScoped<EndWithoutCompletingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "EndWithoutCompleting",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };

        taskService.GetTaskStatusAsync(task.Id).Returns(Task.FromResult<BackgroundTask?>(task));

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

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

        var services = new ServiceCollection();
        services.AddScoped<TestHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "TestTask",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };

        taskService.GetTaskStatusAsync(task.Id).Returns(Task.FromResult<BackgroundTask?>(task));

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);
    }

    [Fact]
    public async Task ExecuteTaskAsync_HandlerResolvesFromOwnScope()
    {
        var taskService = Substitute.For<IBackgroundTaskManagementService>();
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("ScopedTest", typeof(ScopeDependencyTrackingHandler));

        var services = new ServiceCollection();
        services.AddScoped<ScopedDependency>();
        services.AddScoped<ScopeDependencyTrackingHandler>();
        var serviceProvider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var task = new BackgroundTask
        {
            Id = Guid.NewGuid(),
            TaskType = "ScopedTest",
            Status = BackgroundTaskStatus.NotStarted,
            TaskData = "\"test\""
        };

        taskService.GetTaskStatusAsync(task.Id).Returns(Task.FromResult<BackgroundTask?>(task));

        var dbContextFactory = Substitute.For<IDbContextFactory<TestDbContext>>();
        var logger = Substitute.For<ILogger<TaskExecutionManager<TestDbContext>>>();
        var identity = new ExecutionManagerIdentity();
        var gate = new TaskCheckGate();

        var manager = new TaskExecutionManager<TestDbContext>(
            taskService,
            registry,
            serviceProvider,
            config,
            logger,
            dbContextFactory,
            identity,
            gate
        );

        await manager.ExecuteTaskInternalAsync(task);

        await taskService.Received(1).UpdateStatusAsync(task.Id, BackgroundTaskStatus.Completed);
    }
}

