using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class Team : Entity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}
