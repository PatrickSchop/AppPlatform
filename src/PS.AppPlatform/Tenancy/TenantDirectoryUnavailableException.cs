namespace PS.AppPlatform.Tenancy;

/// <summary>Thrown when ITenantDirectory is unavailable (network, timeout, etc).</summary>
public sealed class TenantDirectoryUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
