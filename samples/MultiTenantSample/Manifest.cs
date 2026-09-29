using PS.AppPlatform.Tenancy;

namespace MultiTenantSample;

public sealed class Manifest : AppManifest
{
    public override string Key => "multitenantsample";
    public override TenancyMode Tenancy => TenancyMode.Multi;

    public override IReadOnlyList<AppRoleDefinition> Roles =>
    [
        new("editor", "Editor"),
        new("viewer", "Viewer"),
        new("reporter", "Reporter"),
    ];
}
