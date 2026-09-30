namespace PS.AppPlatform.Tenancy;

public sealed class TenancyOptions
{
    public const string SectionName = "tenancy";

    public TenancyMode Mode { get; set; }

    public string Directory { get; set; } = "management";

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan NullCacheDuration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan StaleIfError { get; set; } = TimeSpan.FromHours(1);

    public string TenantHeader { get; set; } = "X-Tenant-Id";
}
