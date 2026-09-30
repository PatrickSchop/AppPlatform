using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class User : Entity
{
    public string? ObjectId { get; set; }
    public string? IssuerTenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Label only — not used for identity matching.</summary>
    public string? Email { get; set; }
    public bool IsDisabled { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? BoundUtc { get; set; }
}
