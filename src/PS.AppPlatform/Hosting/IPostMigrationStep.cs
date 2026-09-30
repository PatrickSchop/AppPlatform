namespace PS.AppPlatform.Hosting;

/// <summary>Runs after a successful --migrate, in registration order. Non-zero aborts.</summary>
public interface IPostMigrationStep
{
    Task<int> RunAsync(IServiceProvider services, CancellationToken ct);
}
