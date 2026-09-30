using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class Role : Entity
{
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsDeprecated { get; set; }
}
