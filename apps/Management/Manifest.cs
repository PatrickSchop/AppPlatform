using PS.AppPlatform.Tenancy;

namespace PS.Management;

public sealed class Manifest : AppManifest
{
    public override string Key => "management";
    public override string DisplayName => "Application management";
    public override TenancyMode Tenancy => TenancyMode.Single;
    public override IReadOnlyList<AppRoleDefinition> Roles => [new("admin", "Administrator")];
}
