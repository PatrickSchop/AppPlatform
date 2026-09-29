namespace PS.AppPlatform.Tenancy;

public interface ITenantContext
{
    bool IsResolved { get; }
    Guid? UserId { get; }
    Guid? TenantId { get; }
    IReadOnlyList<Guid> TeamIds { get; }
    IReadOnlySet<string> Roles { get; }
}

/// <summary>Scoped. Written once per invocation by the middleware or the task runner.</summary>
public sealed class TenantContext : ITenantContext
{
    private bool _isResolved;
    private Guid? _userId;
    private Guid? _tenantId;
    private IReadOnlyList<Guid> _teamIds = [];
    private IReadOnlySet<string> _roles = new HashSet<string>();

    public bool IsResolved => _isResolved;
    public Guid? UserId => _userId;
    public Guid? TenantId => _tenantId;
    public IReadOnlyList<Guid> TeamIds => _teamIds;
    public IReadOnlySet<string> Roles => _roles;

    public void Set(Guid userId, Guid tenantId, IReadOnlyList<Guid> teamIds, IEnumerable<string> roles)
    {
        if (_isResolved)
        {
            throw new InvalidOperationException("TenantContext.Set() called more than once per request scope");
        }

        _isResolved = true;
        _userId = userId;
        _tenantId = tenantId;
        _teamIds = teamIds;
        _roles = new HashSet<string>(roles, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>A registered user with no tenant selected, for [TenantOptional] endpoints.</summary>
    public void SetUser(Guid userId)
    {
        if (_isResolved)
        {
            throw new InvalidOperationException("TenantContext.SetUser() called more than once per request scope");
        }

        _isResolved = true;
        _userId = userId;
        _tenantId = null;
        _teamIds = [];
        _roles = new HashSet<string>();
    }

    public void SetSystem(Guid tenantId)
    {
        if (_isResolved)
        {
            throw new InvalidOperationException("TenantContext.SetSystem() called more than once per request scope");
        }

        _isResolved = true;
        _userId = null;
        _tenantId = tenantId;
        _teamIds = [];
        _roles = new HashSet<string>();
    }
}
