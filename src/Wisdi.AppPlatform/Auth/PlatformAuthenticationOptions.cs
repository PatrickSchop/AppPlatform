namespace PS.AppPlatform.Auth;

public sealed class PlatformAuthenticationOptions
{
    public const string SectionName = "authentication";

    /// <summary>When false, all authorization passes. Defaults to true when an azureEntraId section exists.</summary>
    public bool? Enabled { get; set; }

    public AzureEntraIdOptions? AzureEntraId { get; set; }

    /// <summary>App Role every caller must hold, e.g. "stock.user". Null disables the role check.</summary>
    public string? RequiredRole { get; set; }

    /// <summary>Function names that are anonymous without needing [AllowAnonymous] in source.</summary>
    public string[] AnonymousFunctions { get; set; } = [];
}

public sealed class AzureEntraIdOptions
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    /// <summary>Extra accepted audiences, e.g. "api://{clientId}".</summary>
    public string[] AdditionalAudiences { get; set; } = [];
}

