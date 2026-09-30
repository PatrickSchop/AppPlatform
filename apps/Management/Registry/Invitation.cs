using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class Invitation : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? AcceptedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}
