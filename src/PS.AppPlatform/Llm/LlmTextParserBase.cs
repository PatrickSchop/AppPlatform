using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace PS.AppPlatform.Llm;

/// <summary>
/// Wrapper class for LLM JSON responses containing a data property
/// </summary>
/// <typeparam name="T">The type of data wrapped in the response</typeparam>
public class LlmResponseWrapper<T>
{
    public T? Data { get; set; }
}

/// <summary>
/// Abstract base class for parsing text using Azure OpenAI LLM with automatic retries and error recovery
/// </summary>
/// <typeparam name="TResult">The type of object that the parser returns</typeparam>
public abstract class LlmTextParserBase<TResult>
    where TResult : class
{
    private readonly ILogger _logger;
    private readonly ILlmTextParseClient _llmClient;
    private readonly string _exampleInput;
    private readonly string _description;
    private readonly int _maxAttempts;
    private readonly string _systemPrompt;
    private readonly TResult _sampleData;

    protected LlmTextParserBase(
        ILlmTextParseClient llmClient,
        ILogger logger,
        string exampleInput,
        string description,
        TResult sampleData,
        int maxAttempts = 3)
    {
        ArgumentNullException.ThrowIfNull(llmClient);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(exampleInput);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(sampleData);

        _logger = logger;
        _llmClient = llmClient;
        _exampleInput = exampleInput;
        _description = description;
        _maxAttempts = maxAttempts;
        _sampleData = sampleData;

        // Build system prompt
        _systemPrompt = BuildSystemPrompt();
    }

    /// <summary>
    /// Parses the input text and returns an object of type TResult with automatic retries on parse errors
    /// </summary>
    /// <param name="inputText">The text to parse</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Parsed object of type TResult</returns>
    public async Task<TResult> ParseAsync(string inputText, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputText);

        var userPrompt = BuildUserPrompt(inputText);
        Exception? lastException = null;
        string lastParseError = "";

        for (int attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                _logger.LogInformation("Parsing text with LLM (attempt {Attempt}/{MaxAttempts})", attempt, _maxAttempts);

                // Include previous parse error in prompt for retry attempts
                var promptWithError = attempt > 1 && !string.IsNullOrEmpty(lastParseError)
                    ? userPrompt + $"\n\nPrevious attempt failed with error: {lastParseError}\nPlease try again, ensuring valid JSON output."
                    : userPrompt;

                // Call LLM client to get JSON response
                var content = await _llmClient.ParseAsync(_systemPrompt, promptWithError, cancellationToken);

                _logger.LogDebug("LLM response: {Content}", content);

                try
                {
                    // Strip fenced code blocks if present
                    var cleanedContent = StripFencedCodeBlock(content);

                    // Parse JSON response as wrapped response
                    var wrappedResponse = JsonSerializer.Deserialize<LlmResponseWrapper<TResult>>(cleanedContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (wrappedResponse == null || wrappedResponse.Data == null)
                    {
                        throw new InvalidOperationException("Failed to deserialize LLM response: Data property was null");
                    }

                    _logger.LogInformation("Successfully parsed text on attempt {Attempt}", attempt);
                    return wrappedResponse.Data;
                }
                catch (JsonException ex)
                {
                    lastException = ex;
                    lastParseError = ex.Message;
                    _logger.LogWarning(ex, "Failed to parse JSON response on attempt {Attempt}/{MaxAttempts}: {Error}",
                        attempt, _maxAttempts, ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Unexpected error parsing text on attempt {Attempt}/{MaxAttempts}", attempt, _maxAttempts);
                throw;
            }
        }

        _logger.LogError("Failed to parse text after {MaxAttempts} attempts", _maxAttempts);
        throw new InvalidOperationException($"Failed to parse text after {_maxAttempts} attempts", lastException);
    }

    protected virtual string BuildSystemPrompt()
    {
        var resultTypeDescription = GetResultTypeDescription();

        var prompt = new StringBuilder();
        prompt.AppendLine("You are a text parsing assistant. Your task is to extract structured data from unstructured text.");
        prompt.AppendLine();
        prompt.AppendLine($"Description: {_description}");
        prompt.AppendLine();
        prompt.AppendLine("You must return the data as a JSON object wrapped in a 'data' property that follows EXACTLY this structure:");
        prompt.AppendLine();
        prompt.AppendLine(resultTypeDescription);
        prompt.AppendLine();
        prompt.AppendLine("CRITICAL REQUIREMENTS:");
        prompt.AppendLine("- Return the results in JSON format following the structure EXACTLY as specified above");
        prompt.AppendLine("- The format is always a single JSON object with a single Data property. The Data property contains the actual data which may be an array of objects or an object.");
        prompt.AppendLine("- Use ONLY the property names and types described in the structure");
        prompt.AppendLine("- Do not add any additional properties or fields not mentioned in the structure");
        prompt.AppendLine("- Return ONLY valid JSON, nothing else");
        prompt.AppendLine("- Do not include any explanatory text, comments, or markdown formatting");
        prompt.AppendLine("- Ensure all properties described in the structure are present in the JSON response");
        prompt.AppendLine("- Use proper JSON syntax with double quotes for property names and string values");
        prompt.AppendLine("- Match the data types exactly as specified (string, number, boolean, array, object)");
        prompt.AppendLine();
        prompt.AppendLine($"Example input text:\n{_exampleInput}");
        prompt.AppendLine();
        prompt.AppendLine("Extract the relevant information from the input text and return it as JSON that matches EXACTLY the structure specified above.");

        return prompt.ToString();
    }

    protected virtual string BuildUserPrompt(string inputText)
    {
        return $"Parse the following text:\n\n{inputText}";
    }

    /// <summary>
    /// Override this method to provide a detailed description of the result type structure for the system prompt.
    /// Default implementation serializes the sample data wrapped in a data property.
    /// </summary>
    protected virtual string GetResultTypeDescription()
    {
        try
        {
            // Create wrapper object with sample data
            var wrapper = new LlmResponseWrapper<TResult> { Data = _sampleData };

            // Serialize the wrapper object to JSON with indentation
            var json = JsonSerializer.Serialize(wrapper, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate result type description, falling back to type name");
            return typeof(TResult).Name;
        }
    }

    /// <summary>
    /// Strips fenced code blocks (```json ... ```) from LLM responses
    /// </summary>
    private static string StripFencedCodeBlock(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        // Match ``` optionally followed by language (e.g., json) and content until closing ```
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```"))
        {
            // Find the end of the opening fence (next newline)
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed.Substring(firstNewline + 1);
            }

            // Remove closing fence
            if (trimmed.EndsWith("```"))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 3);
            }
        }

        return trimmed.Trim();
    }
}

