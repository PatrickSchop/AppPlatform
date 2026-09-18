using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Wisdi.AppPlatform.Endpoints;

public sealed class ConfigurationEndpoints : IConfigurationEndpoints
{
    private readonly ILogger<ConfigurationEndpoints> _logger;
    private readonly IConfiguration _configuration;

    public ConfigurationEndpoints(ILogger<ConfigurationEndpoints> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public Task<IActionResult> GetWebAppConfigurationAsync(HttpRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting webApp configuration");

        try
        {
            var webAppConfig = _configuration.GetSection("webApp");

            if (!webAppConfig.Exists())
            {
                return Task.FromResult<IActionResult>(new OkObjectResult(new { }));
            }

            var webAppObject = ConvertConfigurationSectionToObject(webAppConfig);
            return Task.FromResult<IActionResult>(new OkObjectResult(webAppObject));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webApp configuration");
            return Task.FromResult<IActionResult>(new StatusCodeResult(500));
        }
    }

    private object ConvertConfigurationSectionToObject(IConfigurationSection section)
    {
        var children = section.GetChildren().ToList();

        if (!children.Any())
        {
            // Leaf value
            return section.Value ?? string.Empty;
        }

        // Check if this looks like an array (all-numeric consecutive keys starting at "0")
        if (IsArrayLike(children))
        {
            var arrayItems = children
                .OrderBy(c => int.Parse(c.Key))
                .Select(c => ConvertConfigurationSectionToObject(c))
                .ToList();
            return arrayItems;
        }

        // Object with properties
        var result = new Dictionary<string, object>();
        foreach (var child in children)
        {
            result[child.Key] = ConvertConfigurationSectionToObject(child);
        }

        return result;
    }

    private bool IsArrayLike(List<IConfigurationSection> children)
    {
        if (children.Count == 0)
            return false;

        // Check if all keys are numeric
        var keys = children.Select(c => c.Key).ToList();
        if (!keys.All(k => int.TryParse(k, out _)))
            return false;

        // Check if keys are consecutive starting at 0
        var numericKeys = keys
            .Select(k => int.Parse(k))
            .OrderBy(k => k)
            .ToList();

        for (int i = 0; i < numericKeys.Count; i++)
        {
            if (numericKeys[i] != i)
                return false;
        }

        return true;
    }
}
