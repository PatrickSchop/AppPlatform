namespace PS.AppPlatform.Tenancy;

public enum TenancyMode
{
    None,   // v1 behaviour: authentication only, no registry
    Single, // registry-backed users and roles; exactly one tenant, always resolved
    Multi   // registry-backed; users may belong to several tenants
}
