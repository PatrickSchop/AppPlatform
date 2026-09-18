namespace Wisdi.AppPlatform.Llm;

/// <summary>
/// Interface for making LLM text parsing requests to Azure OpenAI
/// </summary>
public interface ILlmTextParseClient
{
    /// <summary>
    /// Sends a request to Azure OpenAI with the given system and user prompts
    /// </summary>
    /// <param name="systemPrompt">The system prompt</param>
    /// <param name="userPrompt">The user prompt</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The JSON response from the LLM</returns>
    Task<string> ParseAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default);
}
