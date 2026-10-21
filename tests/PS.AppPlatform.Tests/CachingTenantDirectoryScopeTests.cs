using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class CachingTenantDirectoryScopeTests
{
    private sealed class ScopedResource : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    // Stands in for a directory that holds a scoped DbContext.
    private sealed class ScopedDirectory(ScopedResource resource) : ITenantDirectory
    {
        public async Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
        {
            await Task.Yield();
            ObjectDisposedException.ThrowIf(resource.Disposed, resource);
            return new UserMemberships(Guid.NewGuid(), "Test", []);
        }
    }

    [Fact]
    public async Task Lookup_RunsInsideTheScopeOfTheInnerDirectory()
    {
        var services = new ServiceCollection();
        services.AddScoped<ScopedResource>();
        services.AddScoped<ScopedDirectory>();
        using var provider = services.BuildServiceProvider();

        var directory = new CachingTenantDirectory(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new TenancyOptions()),
            NullLogger<CachingTenantDirectory>.Instance,
            typeof(ScopedDirectory));

        var result = await directory.GetMembershipsAsync(new IdentityKey("oid", "tid"), CancellationToken.None);

        Assert.NotNull(result);
    }
}
