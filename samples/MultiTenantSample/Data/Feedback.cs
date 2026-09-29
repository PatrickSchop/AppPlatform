using PS.AppPlatform.Data;

namespace MultiTenantSample.Data;

public class Feedback : TenantEntity
{
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
