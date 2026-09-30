using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class TeamMember : Entity
{
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
}
