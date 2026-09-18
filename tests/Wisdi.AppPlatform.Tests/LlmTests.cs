using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using Wisdi.AppPlatform.Llm;
using Xunit;

namespace Wisdi.AppPlatform.Tests;

public class LlmTests
{
    private static ILogger<T> CreateLogger<T>()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var sp = services.BuildServiceProvider();
        return sp.GetRequiredService<ILogger<T>>();
    }

    [Fact]
    public async Task WellFormedJson_deserializes_on_first_attempt()
    {
        // Arrange
        var mockClient = new TestLlmParseClient("{\"data\": {\"id\": \"123\", \"name\": \"Test\"}}");
        var sampleData = new TestData { Id = "0", Name = "" };
        var parser = new TestParser(mockClient, sampleData);

        // Act
        var result = await parser.ParseAsync("test input");

        // Assert
        Assert.Single(mockClient.CallHistory);
        Assert.Equal("123", result.Id);
        Assert.Equal("Test", result.Name);
    }

    [Fact]
    public async Task MalformedThenWellFormed_succeeds_on_second_attempt()
    {
        // Arrange
        var mockClient = new TestLlmParseClient(
            "{invalid json}",
            "{\"data\": {\"id\": \"456\", \"name\": \"Success\"}}"
        );
        var sampleData = new TestData { Id = "0", Name = "" };
        var parser = new TestParser(mockClient, sampleData, maxAttempts: 3);

        // Act
        var result = await parser.ParseAsync("test input");

        // Assert
        Assert.Equal(2, mockClient.CallHistory.Count);
        // Second prompt should contain error information
        Assert.Contains("Previous attempt failed", mockClient.CallHistory[1]);
        Assert.Equal("456", result.Id);
        Assert.Equal("Success", result.Name);
    }

    [Fact]
    public async Task ThreeFailures_throws_after_max_attempts()
    {
        // Arrange
        var mockClient = new TestLlmParseClient(
            "{invalid 1}",
            "{invalid 2}",
            "{invalid 3}"
        );
        var sampleData = new TestData { Id = "0", Name = "" };
        var parser = new TestParser(mockClient, sampleData, maxAttempts: 3);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => parser.ParseAsync("test input"));
        Assert.Equal(3, mockClient.CallHistory.Count);
    }

    [Fact]
    public async Task FencedJsonBlock_parses_without_retry()
    {
        // Arrange
        var jsonWithFence = "```json\n{\"data\": {\"id\": \"789\", \"name\": \"Fenced\"}}\n```";
        var mockClient = new TestLlmParseClient(jsonWithFence);
        var sampleData = new TestData { Id = "0", Name = "" };
        var parser = new TestParser(mockClient, sampleData);

        // Act
        var result = await parser.ParseAsync("test input");

        // Assert
        Assert.Single(mockClient.CallHistory);
        Assert.Equal("789", result.Id);
        Assert.Equal("Fenced", result.Name);
    }

    [Fact]
    public void LlmServiceBuilder_registers_nothing_when_azureOpenAI_absent()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddLlmServices(config);
        var sp = services.BuildServiceProvider();

        // Assert
        var openAiClient = sp.GetService<Azure.AI.OpenAI.AzureOpenAIClient>();
        Assert.Null(openAiClient);
    }

    [Fact]
    public void LlmServiceBuilder_throws_when_azureOpenAI_missing_endpoint()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["azureOpenAI:authentication:type"] = "managedIdentity",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Wisdi.AppPlatform.Hosting.IAzureIdentityProvider>(
            NSubstitute.Substitute.For<Wisdi.AppPlatform.Hosting.IAzureIdentityProvider>());

        services.AddLlmServices(config);
        var sp = services.BuildServiceProvider();

        // Act & Assert - should throw when trying to resolve AzureOpenAIClient
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<Azure.AI.OpenAI.AzureOpenAIClient>());
    }

    [Fact]
    public void HtmlToXhtmlConverter_adds_xml_declaration()
    {
        // Arrange
        var html = "<html><body>Test</body></html>";

        // Act
        var result = HtmlToXhtmlConverter.ConvertToXhtml(html);

        // Assert
        Assert.StartsWith("<?xml", result);
        Assert.Contains("xmlns=\"http://www.w3.org/1999/xhtml\"", result);
    }

    [Fact]
    public void HtmlToXhtmlConverter_closes_self_closing_tags()
    {
        // Arrange
        var html = "<html><body><br><img src=\"test.jpg\"><hr></body></html>";

        // Act
        var result = HtmlToXhtmlConverter.ConvertToXhtml(html);

        // Assert
        Assert.Contains("<br ", result);
        Assert.Contains("<img ", result);
        Assert.Contains("<hr ", result);
        // All should be self-closed
        Assert.Contains("/>", result);
    }

    [Fact]
    public void HtmlToXhtmlConverter_escapes_ampersands()
    {
        // Arrange
        var html = "<html><body>Tom & Jerry</body></html>";

        // Act
        var result = HtmlToXhtmlConverter.ConvertToXhtml(html);

        // Assert
        Assert.Contains("Tom &amp; Jerry", result);
    }

    [Fact]
    public void HtmlToXhtmlConverter_fixes_boolean_attributes()
    {
        // Arrange
        var html = "<input type=\"checkbox\" checked>";

        // Act
        var result = HtmlToXhtmlConverter.ConvertToXhtml(html);

        // Assert
        Assert.Contains("checked=\"checked\"", result);
    }

    // Test data structures and helpers
    public class TestData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class TestParser : LlmTextParserBase<TestData>
    {
        public TestParser(ILlmTextParseClient client, TestData sampleData, int maxAttempts = 3)
            : base(
                client,
                CreateLogger<TestParser>(),
                "Sample input",
                "Extract test data",
                sampleData,
                maxAttempts)
        {
        }
    }

    public class TestLlmParseClient : ILlmTextParseClient
    {
        private readonly Queue<string> _responses;
        public List<string> CallHistory { get; } = new();

        public TestLlmParseClient(params string[] responses)
        {
            _responses = new Queue<string>(responses);
        }

        public Task<string> ParseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
        {
            CallHistory.Add(userPrompt);
            if (_responses.Count == 0)
                throw new InvalidOperationException("No more responses available");
            return Task.FromResult(_responses.Dequeue());
        }
    }
}
