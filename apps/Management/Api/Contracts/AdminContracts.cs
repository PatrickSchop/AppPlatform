namespace PS.Management.Api.Contracts;

// Requests
public record PatchApplicationRequest(string? DisplayName, bool? IsDisabled);
public record CreateTenantRequest(string Name);
public record PatchTenantRequest(string? Name, bool? IsDisabled);
public record CreateTeamRequest(string Name);
public record PatchTeamRequest(string? Name);
public record CreateUserRequest(string DisplayName, string? Email);
public record PatchUserRequest(string? DisplayName, string? Email, bool? IsDisabled);
public record AddMemberRequest(Guid UserId, List<Guid> RoleIds);
public record SetRolesRequest(List<Guid> RoleIds);

// Responses
public record ApplicationSummary(Guid Id, string Key, string DisplayName, PS.AppPlatform.Tenancy.TenancyMode Tenancy, int TenantCount, int UserCount, bool IsDisabled);
public record ApplicationDetail(Guid Id, string Key, string DisplayName, PS.AppPlatform.Tenancy.TenancyMode Tenancy, bool IsDisabled, List<RoleSummary> Roles, List<TenantSummary> Tenants);
public record RoleSummary(Guid Id, string Name, string DisplayName, bool IsDeprecated, int AssignmentCount);
public record TenantSummary(Guid Id, string Name, bool IsDisabled);
public record TeamSummary(Guid Id, string Name, bool IsDefault);
public record UserSummary(Guid Id, string DisplayName, string? Email, string Status, DateTime CreatedUtc);
public record UserDetail(Guid Id, string DisplayName, string? Email, string Status, DateTime CreatedUtc, List<UserMembershipInfo> Memberships);
public record UserMembershipInfo(string AppKey, string TenantName, string TeamName, Guid MemberId, List<string> Roles);
public record MemberSummary(Guid MemberId, Guid UserId, string DisplayName, List<string> Roles);
