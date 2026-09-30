using PS.AppPlatform.Data;

namespace PS.Management.Registry;

public class RoleAssignment : Entity
{
    public Guid TeamMemberId { get; set; }
    public Guid RoleId { get; set; }
}
