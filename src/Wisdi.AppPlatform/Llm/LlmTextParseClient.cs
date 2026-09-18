using Azure.AI.OpenAI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;

namespace Wisdi.AppPlatform.Llm;

/// <summary>
/// Client for making LLM text parsing requests to Azure OpenAI
/// </summary>
public class LlmTextParseClient : ILlmTextParseClient
{
    private readonly ILogger _logger;
    private readonly AzureOpenAIClient _openAIClient;
    private readonly string _deploymentName;

    public LlmTextParseClient(
        AzureOpenAIClient openAIClient,
        IConfiguration configuration,
        ILogger<LlmTextParseClient> logger)
    {
        ArgumentNullException.ThrowIfNull(openAIClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _openAIClient = openAIClient;

        // Get deployment name from azureOpenAI config
        var azureOpenAIConfig = configuration.GetSection("azureOpenAI");
        _deploymentName = azureOpenAIConfig.GetValue<string>("deployment") ?? "gpt-4o-mini";
    }

    public async Task<string> ParseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);

        _logger.LogInformation("Parsing text with LLM");

        var chatClient = _openAIClient.GetChatClient(_deploymentName);
        var chatMessages = new List<ChatMessage>()
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt)
        };
        var response = await chatClient.CompleteChatAsync(
            chatMessages,
            new ChatCompletionOptions()
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
            },
            cancellationToken);

        if (response.Value.Content.Count == 0)
        {
            throw new InvalidOperationException("LLM returned no content");
        }

        var content = response.Value.Content[0].Text;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("LLM returned empty content");
        }

        _logger.LogDebug("LLM response: {Content}", content);
        _logger.LogInformation("Successfully parsed text");
        return content;
    }
}
