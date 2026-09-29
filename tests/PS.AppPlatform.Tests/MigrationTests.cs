using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PS.AppPlatform.Data;
using PS.AppPlatform.Hosting;
using Xunit;

namespace PS.AppPlatform.Tests;

public class MigrationTests
{
    [Fact]
    public void Migration_service_graph_resolves_the_identity_provider()
    {
        // A deployed app authenticates to SQL with a managed identity, so a migration run
        // must construct AzureIdentityProvider, which takes IConfiguration. While the
        // migration service collection did not register IConfiguration, --migrate threw
        // before reaching the database, so no deployed migration could ever run.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["database:connectionString"] = "Server=tcp:example.database.windows.net,1433;Database=x;Encrypt=True;",
                ["database:useManagedIdentity"] = "true",
                ["azureIdentity:type"] = "azureCli",
            })
            .Build();

        var services = MigrationEntryPoint.BuildMigrationServices<TestMigrationContext>(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAzureIdentityProvider>());
        Assert.NotNull(provider.GetRequiredService<IConfiguration>());
    }

    private class TestMigrationContext : PlatformDbContext
    {
        public TestMigrationContext(DbContextOptions<TestMigrationContext> options, PlatformAssemblies assemblies)
            : base(options, assemblies) { }
    }

    [Fact]
    public void EmbeddedScriptsAreDiscovered()
    {
        var provider = new EmbeddedMigrationScriptProvider();
        var scripts = provider.GetScripts().ToList();

        Assert.NotEmpty(scripts);
        Assert.Contains(scripts, s => s.Name == "000_CreateSchemaVersions.sql");
        Assert.Contains(scripts, s => s.Name == "010_CreateBackgroundTasks.sql");
    }

    [Fact]
    public void BatchSplitterHandlesGOCorrectly()
    {
        var migrator = CreateMigrator();
        var batches = migrator.SplitBatches("SELECT 1\nGO\nSELECT 2");

        Assert.Equal(2, batches.Count);
        Assert.Contains("SELECT 1", batches[0]);
        Assert.Contains("SELECT 2", batches[1]);
    }

    [Fact]
    public void BatchSplitterIgnoresCaseInsensitiveGO()
    {
        var migrator = CreateMigrator();
        var batches = migrator.SplitBatches("SELECT 1\ngo\nSELECT 2");

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void BatchSplitterSkipsEmptyBatches()
    {
        var migrator = CreateMigrator();
        var batches = migrator.SplitBatches("SELECT 1\nGO\nGO\nSELECT 2");

        Assert.Equal(2, batches.Count);
    }

    [Fact]
    public void BatchSplitterHandlesTrailingGO()
    {
        var migrator = CreateMigrator();
        var batches = migrator.SplitBatches("SELECT 1\nGO");

        Assert.Single(batches);
        Assert.Contains("SELECT 1", batches[0]);
    }

    [Fact]
    public async Task CoreAndAppScriptsAreOrderedCorrectly()
    {
        var coreProvider = new FakeMigrationScriptProvider(new[]
        {
            ("050_core.sql", "core"),
            ("010_core.sql", "core")
        });

        var appProvider = new FakeMigrationScriptProvider(new[]
        {
            ("110_app.sql", "app"),
            ("100_app.sql", "app")
        });

        var migrator = CreateMigrator(new[] { coreProvider, appProvider });
        var scripts = await migrator.GetOrderedScripts();

        var names = scripts.Select(s => s.Name).ToList();
        Assert.Equal(new[] { "010_core.sql", "050_core.sql", "100_app.sql", "110_app.sql" }, names);
    }

    [Fact]
    public async Task ValidationFailsForCoreScriptWithInvalidNumber()
    {
        var provider = new FakeMigrationScriptProvider(new[] { ("100_bad.sql", "core") });
        var migrator = CreateMigrator(new[] { provider });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await migrator.InitializeDatabaseAsync());
    }

    [Fact]
    public async Task ValidationFailsForAppScriptBelow100()
    {
        var provider = new FakeMigrationScriptProvider(new[] { ("090_bad.sql", "app") });
        var migrator = CreateMigrator(new[] { provider });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await migrator.InitializeDatabaseAsync());
    }

    [Fact]
    public async Task ValidationFailsForDuplicateScriptNames()
    {
        var provider1 = new FakeMigrationScriptProvider(new[] { ("100_dup.sql", "core") });
        var provider2 = new FakeMigrationScriptProvider(new[] { ("100_dup.sql", "app") });

        var migrator = CreateMigrator(new[] { provider1, provider2 });

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await migrator.InitializeDatabaseAsync());
    }

    [Fact]
    public void ChecksumIsStableAnd64HexCharacters()
    {
        var migrator = CreateMigrator();
        var content = "SELECT 1";

        var checksum1 = migrator.ComputeChecksum(content);
        var checksum2 = migrator.ComputeChecksum(content);

        Assert.Equal(checksum1, checksum2);
        Assert.Equal(64, checksum1.Length);
        Assert.True(checksum1.All(c => "0123456789abcdef".Contains(c)));
    }

    private TestDatabaseMigrator CreateMigrator(IMigrationScriptProvider[]? providers = null)
    {
        providers ??= new IMigrationScriptProvider[] { new FakeMigrationScriptProvider(Array.Empty<(string, string)>()) };
        return new TestDatabaseMigrator(providers);
    }

    private class FakeMigrationScriptProvider : IMigrationScriptProvider
    {
        private readonly (string name, string source)[] _scripts;

        public FakeMigrationScriptProvider((string name, string source)[] scripts)
        {
            _scripts = scripts;
        }

        public IEnumerable<MigrationScript> GetScripts()
        {
            return _scripts.Select(s => new MigrationScript(
                s.name,
                s.source,
                ct => Task.FromResult($"-- {s.name}")));
        }
    }

    private class TestDatabaseMigrator
    {
        private readonly IEnumerable<IMigrationScriptProvider> _providers;

        public TestDatabaseMigrator(IEnumerable<IMigrationScriptProvider> providers)
        {
            _providers = providers;
        }

        public List<string> SplitBatches(string content) =>
            SplitBatchesInternal(content);

        public async Task<List<MigrationScript>> GetOrderedScripts() =>
            await GetOrderedScriptsInternal();

        public async Task InitializeDatabaseAsync() =>
            await ValidateAsync();

        public string ComputeChecksum(string content) =>
            ComputeChecksumInternal(content);

        private List<string> SplitBatchesInternal(string content)
        {
            var batches = new List<string>();
            var currentBatch = new System.Text.StringBuilder();

            using var reader = new StringReader(content);
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.Equals("GO", StringComparison.OrdinalIgnoreCase))
                {
                    var batch = currentBatch.ToString().Trim();
                    if (!string.IsNullOrEmpty(batch))
                    {
                        batches.Add(batch);
                    }

                    currentBatch.Clear();
                }
                else
                {
                    if (currentBatch.Length > 0)
                    {
                        currentBatch.AppendLine();
                    }

                    currentBatch.Append(line);
                }
            }

            var finalBatch = currentBatch.ToString().Trim();
            if (!string.IsNullOrEmpty(finalBatch))
            {
                batches.Add(finalBatch);
            }

            return batches;
        }

        private async Task<List<MigrationScript>> GetOrderedScriptsInternal()
        {
            var coreScripts = new List<MigrationScript>();
            var appScripts = new List<MigrationScript>();

            foreach (var provider in _providers)
            {
                var scripts = provider.GetScripts();
                foreach (var script in scripts)
                {
                    if (script.Source == "core")
                    {
                        coreScripts.Add(script);
                    }
                    else
                    {
                        appScripts.Add(script);
                    }
                }
            }

            coreScripts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            appScripts.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

            var allScripts = new List<MigrationScript>(coreScripts);
            allScripts.AddRange(appScripts);

            return allScripts;
        }

        private async Task ValidateAsync()
        {
            var scripts = await GetOrderedScriptsInternal();

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var script in scripts)
            {
                if (!seenNames.Add(script.Name))
                {
                    throw new InvalidOperationException($"Duplicate script name: {script.Name}");
                }

                var leadingNumber = ExtractLeadingNumber(script.Name);

                if (script.Source == "core")
                {
                    if (leadingNumber < 0 || leadingNumber > 99)
                    {
                        throw new InvalidOperationException(
                            $"Core script '{script.Name}' has invalid number: must be 000-099");
                    }
                }
                else if (script.Source == "app")
                {
                    if (leadingNumber < 100)
                    {
                        throw new InvalidOperationException(
                            $"App script '{script.Name}' has invalid number: must be 100+");
                    }
                }
            }
        }

        private int ExtractLeadingNumber(string scriptName)
        {
            var match = System.Text.RegularExpressions.Regex.Match(scriptName, @"^(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var num))
            {
                return num;
            }

            return -1;
        }

        private string ComputeChecksumInternal(string content)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}

