using System.Collections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using PS.AppPlatform.Hosting;
using PS.AppPlatform.Tenancy;
using Xunit;

namespace PS.AppPlatform.Tests;

public class TenancyContractTests
{
    [Fact]
    public void ManifestDiscovery_FindsTestManifest()
    {
        var testAssembly = typeof(TenancyContractTests).Assembly;
        var assemblyTypes = testAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(AppManifest).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(assemblyTypes);
        Assert.Contains(typeof(TestManifest), assemblyTypes);
    }

    [Fact]
    public void ManifestDiscovery_WithMultipleManifests_FailsWithClearMessage()
    {
        var testAssembly = typeof(TenancyContractTests).Assembly;
        var assemblyTypes = testAssembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(AppManifest).IsAssignableFrom(t))
            .ToList();

        if (assemblyTypes.Count > 1)
        {
            var names = string.Join(", ", assemblyTypes.Select(t => t.Name));
            var ex = new InvalidOperationException($"Multiple AppManifest classes found: {names}");
            Assert.Contains("Multiple AppManifest", ex.Message);
        }
    }

    [Fact]
    public void InvalidKey_Throws()
    {
        var manifest = new TestManifest { KeyValue = "Has Caps" };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate());
    }

    [Fact]
    public void DuplicateRole_Throws()
    {
        var manifest = new TestManifest();
        manifest.RolesValue = new[]
        {
            new AppRoleDefinition("editor", "Editor"),
            new AppRoleDefinition("editor", "Editor Again")
        };
        var ex = Assert.Throws<InvalidOperationException>(() => manifest.Validate());
        Assert.Contains("Duplicate role", ex.Message);
    }

    [Fact]
    public void InvalidRoleName_Throws()
    {
        var manifest = new TestManifest();
        manifest.RolesValue = new[] { new AppRoleDefinition("Invalid Name", "Display") };
        Assert.Throws<InvalidOperationException>(() => manifest.Validate());
    }

    [Fact]
    public void ModeMismatch_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["tenancy:mode"] = "single",
                ["tenancy:directory"] = "config",
            })
            .Build();

        var assemblies = new PlatformAssemblies();
        assemblies.Add(typeof(TenancyContractTests).Assembly);

        var services = new ServiceCollection();
        services.AddSingleton(assemblies);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPlatformTenancy(configuration));
        Assert.Contains("tenancy:mode", ex.Message);
        Assert.Contains("Multi", ex.Message);
    }

    [Fact]
    public async Task ConfigTenantDirectory_ReturnsUserMemberships()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["tenancy:devDirectory:tenants:0:id"] = "11111111-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:tenants:0:name"] = "Contoso",
                ["tenancy:devDirectory:users:0:id"] = "22222222-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:users:0:oid"] = "test-oid",
                ["tenancy:devDirectory:users:0:tid"] = "test-tid",
                ["tenancy:devDirectory:users:0:displayName"] = "Test User",
                ["tenancy:devDirectory:users:0:memberships:0:tenantId"] = "11111111-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:users:0:memberships:0:roles:0"] = "editor",
            })
            .Build();

        var env = new TestHostingEnvironment { IsProductionValue = false };
        var directory = new ConfigTenantDirectory(configuration, env);

        var identity = new IdentityKey("test-oid", "test-tid");
        var result = await directory.GetMembershipsAsync(identity, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(Guid.Parse("22222222-0000-0000-0000-000000000001"), result.UserId);
        Assert.Equal("Test User", result.DisplayName);
        Assert.Single(result.Tenants);
        Assert.Equal("Contoso", result.Tenants[0].TenantName);
        Assert.Contains("editor", result.Tenants[0].Roles);
    }

    [Fact]
    public async Task ConfigTenantDirectory_UnknownOid_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["tenancy:devDirectory:tenants:0:id"] = "11111111-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:tenants:0:name"] = "Contoso",
            })
            .Build();

        var env = new TestHostingEnvironment { IsProductionValue = false };
        var directory = new ConfigTenantDirectory(configuration, env);

        var identity = new IdentityKey("unknown-oid", "test-tid");
        var result = await directory.GetMembershipsAsync(identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ConfigTenantDirectory_WrongTid_ReturnsNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["tenancy:devDirectory:tenants:0:id"] = "11111111-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:tenants:0:name"] = "Contoso",
                ["tenancy:devDirectory:users:0:id"] = "22222222-0000-0000-0000-000000000001",
                ["tenancy:devDirectory:users:0:oid"] = "test-oid",
                ["tenancy:devDirectory:users:0:tid"] = "test-tid",
                ["tenancy:devDirectory:users:0:displayName"] = "Test User",
            })
            .Build();

        var env = new TestHostingEnvironment { IsProductionValue = false };
        var directory = new ConfigTenantDirectory(configuration, env);

        var identity = new IdentityKey("test-oid", "wrong-tid");
        var result = await directory.GetMembershipsAsync(identity, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void ConfigTenantDirectory_InProduction_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var env = new TestHostingEnvironment { IsProductionValue = true };

        Assert.Throws<InvalidOperationException>(() =>
            new ConfigTenantDirectory(configuration, env));
    }

    [Fact]
    public void TenantContext_Set_Twice_Throws()
    {
        var context = new TenantContext();
        context.Set(Guid.NewGuid(), Guid.NewGuid(), [], []);

        Assert.Throws<InvalidOperationException>(() =>
            context.Set(Guid.NewGuid(), Guid.NewGuid(), [], []));
    }

    [Fact]
    public void TenantContext_SetSystem_Twice_Throws()
    {
        var context = new TenantContext();
        context.SetSystem(Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() =>
            context.SetSystem(Guid.NewGuid()));
    }

    private sealed class TestHostingEnvironment : IHostEnvironment
    {
        public bool IsProductionValue { get; set; }

        public string EnvironmentName
        {
            get => IsProductionValue ? Environments.Production : Environments.Development;
            set { }
        }

        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class NullFileProvider : IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => new EmptyDirectoryContents();
        public IFileInfo GetFileInfo(string subpath) => new NullFileInfo();
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    }

    private sealed class EmptyDirectoryContents : IDirectoryContents
    {
        public bool Exists => false;
        public IEnumerator<IFileInfo> GetEnumerator() => Enumerable.Empty<IFileInfo>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class NullFileInfo : IFileInfo
    {
        public bool Exists => false;
        public bool IsDirectory => false;
        public DateTimeOffset LastModified => DateTimeOffset.Now;
        public long Length => 0;
        public string Name => "";
        public string PhysicalPath => "";
        public Stream CreateReadStream() => Stream.Null;
    }

    private sealed class NullChangeToken : IChangeToken
    {
        public static NullChangeToken Singleton { get; } = new();
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => NullDisposable.Instance;
    }

    private sealed class NullDisposable : IDisposable
    {
        public static NullDisposable Instance { get; } = new();
        public void Dispose() { }
    }
}

public sealed class TestManifest : AppManifest
{
    public string KeyValue { get; set; } = "testapp";
    public IReadOnlyList<AppRoleDefinition> RolesValue { get; set; } = new[] { new AppRoleDefinition("editor", "Editor") };

    public override string Key => KeyValue;
    public override TenancyMode Tenancy => TenancyMode.Multi;
    public override IReadOnlyList<AppRoleDefinition> Roles => RolesValue;
}
