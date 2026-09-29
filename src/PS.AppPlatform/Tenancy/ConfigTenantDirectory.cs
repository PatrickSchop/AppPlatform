using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace PS.AppPlatform.Tenancy;

public sealed class ConfigTenantDirectory : ITenantDirectory
{
    private readonly Dictionary<string, TenantInfo> _tenants;
    private readonly Dictionary<string, UserInfo> _users;

    public ConfigTenantDirectory(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.EnvironmentName == Environments.Production)
        {
            throw new InvalidOperationException(
                "ConfigTenantDirectory cannot run in production. Check tenancy:directory setting.");
        }

        _tenants = new Dictionary<string, TenantInfo>();
        _users = new Dictionary<string, UserInfo>();

        var devConfig = configuration.GetSection("tenancy:devDirectory");

        var tenantsSection = devConfig.GetSection("tenants");
        if (tenantsSection.Exists())
        {
            foreach (var tenantConfig in tenantsSection.GetChildren())
            {
                if (Guid.TryParse(tenantConfig["id"], out var id) && tenantConfig["name"] is string name)
                {
                    _tenants[id.ToString()] = new TenantInfo { Id = id, Name = name };
                }
            }
        }

        var usersSection = devConfig.GetSection("users");
        if (usersSection.Exists())
        {
            foreach (var userConfig in usersSection.GetChildren())
            {
                if (Guid.TryParse(userConfig["id"], out var userId) &&
                    userConfig["oid"] is string oid &&
                    userConfig["tid"] is string tid &&
                    userConfig["displayName"] is string displayName)
                {
                    var membershipsSection = userConfig.GetSection("memberships");
                    var memberships = new List<MembershipInfo>();

                    if (membershipsSection.Exists())
                    {
                        foreach (var membershipConfig in membershipsSection.GetChildren())
                        {
                            if (Guid.TryParse(membershipConfig["tenantId"], out var tenantId))
                            {
                                var rolesArray = membershipConfig.GetSection("roles").Get<string[]>() ?? [];
                                memberships.Add(new MembershipInfo
                                {
                                    TenantId = tenantId,
                                    Roles = rolesArray
                                });
                            }
                        }
                    }

                    var key = $"{oid}|{tid}";
                    _users[key] = new UserInfo
                    {
                        Id = userId,
                        DisplayName = displayName,
                        Memberships = memberships
                    };
                }
            }
        }
    }

    public Task<UserMemberships?> GetMembershipsAsync(IdentityKey identity, CancellationToken ct)
    {
        var key = $"{identity.ObjectId}|{identity.IssuerTenantId}";

        if (!_users.TryGetValue(key, out var user))
        {
            return Task.FromResult<UserMemberships?>(null);
        }

        var tenantMemberships = new List<TenantMembership>();

        foreach (var membership in user.Memberships)
        {
            if (_tenants.TryGetValue(membership.TenantId.ToString(), out var tenant))
            {
                var syntheticTeamId = DeterministicTeamId(membership.TenantId);
                tenantMemberships.Add(new TenantMembership(
                    membership.TenantId,
                    tenant.Name,
                    [syntheticTeamId],
                    membership.Roles));
            }
        }

        var result = new UserMemberships(user.Id, user.DisplayName, tenantMemberships);
        return Task.FromResult<UserMemberships?>(result);
    }

    private static Guid DeterministicTeamId(Guid tenantId)
    {
        var bytes = tenantId.ToByteArray();
        bytes[0] = 0xaa;
        bytes[1] = 0xaa;
        bytes[2] = 0xaa;
        bytes[3] = 0xaa;
        return new Guid(bytes);
    }

    private class TenantInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
    }

    private class UserInfo
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = "";
        public List<MembershipInfo> Memberships { get; set; } = [];
    }

    private class MembershipInfo
    {
        public Guid TenantId { get; set; }
        public string[] Roles { get; set; } = [];
    }
}
