using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Wisdi.AppPlatform.Tasks;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class TaskContractTests
{
    [Fact]
    public void BackgroundTaskStatus_Running_HasExecutingFlag()
    {
        var result = BackgroundTaskStatus.Running & BackgroundTaskStatus.ExecutingFlag;
        Assert.True(result != 0);
    }

    [Fact]
    public void BackgroundTaskStatus_Completed_DoesNotHaveExecutingFlag()
    {
        var result = BackgroundTaskStatus.Completed & BackgroundTaskStatus.ExecutingFlag;
        Assert.True(result == 0);
    }

    [Fact]
    public void BackgroundTaskStatus_Failed_HasCompletedFlag()
    {
        var result = BackgroundTaskStatus.Failed & BackgroundTaskStatus.CompletedFlag;
        Assert.True(result != 0);
    }

    [Fact]
    public void TaskHandlerRegistry_ResolvesCaseInsensitive()
    {
        var registry = new TaskHandlerRegistry();
        registry.RegisterHandler("MyTask", typeof(TestHandler));

        var resolved = registry.GetHandlerType("mytask");
        Assert.Equal(typeof(TestHandler), resolved);
    }

    [Fact]
    public void BackgroundTaskCollection_ThrowsOnDuplicateNameDifferentType()
    {
        var collection = new BackgroundTaskCollection();
        collection.AddBackgroundTask("Task1", typeof(TestHandler));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            collection.AddBackgroundTask("Task1", typeof(AnotherTestHandler))
        );
        Assert.Contains("already registered", ex.Message);
    }

    [Fact]
    public void BackgroundTaskCollection_AllowsSameNameSameType()
    {
        var collection = new BackgroundTaskCollection();
        collection.AddBackgroundTask("Task1", typeof(TestHandler));
        collection.AddBackgroundTask("Task1", typeof(TestHandler));
        // Should not throw
    }

    [Fact]
    public void BackgroundTaskCollection_RegistersHandlersAndBuildsRegistry()
    {
        var services = new ServiceCollection();
        var collection = new BackgroundTaskCollection();
        collection.AddBackgroundTask<TestHandler>("MyTask");

        collection.RegisterBackgroundTaskHandlers(services);

        var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITaskHandlerRegistry>();

        Assert.NotNull(registry.GetHandlerType("MyTask"));
    }

    [Fact]
    public async Task TaskHandlerContext_UpdateProgressAsync_Clamps()
    {
        var mockService = Substitute.For<IBackgroundTaskManagementService>();
        var taskId = Guid.NewGuid();
        var context = new TaskHandlerContext(mockService, taskId);

        await context.UpdateProgressAsync(150);

        await mockService.Received(1).UpdateProgressAsync(taskId, 100);
    }

    [Fact]
    public async Task TaskHandlerContext_UpdateProgressAsync_RenewsLease()
    {
        var mockService = Substitute.For<IBackgroundTaskManagementService>();
        var taskId = Guid.NewGuid();
        var context = new TaskHandlerContext(mockService, taskId);

        await context.UpdateProgressAsync(50);

        await mockService.Received(1).RenewLeaseAsync(taskId, default);
    }

    private class TestHandler : ITaskHandler<string>
    {
        public Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private class AnotherTestHandler : ITaskHandler<string>
    {
        public Task HandleAsync(BackgroundTask task, string taskData, TaskHandlerContext context, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
