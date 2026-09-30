using PS.AppPlatform.Data;
using PS.AppPlatform.Tenancy;

namespace PS.Management.Registry;

public class Application : Entity
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public TenancyMode Tenancy { get; set; }
    public Guid? ServicePrincipalId { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime RegisteredUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
