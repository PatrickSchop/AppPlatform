using PS.AppPlatform.Tenancy;

namespace TinyApp;

/// <summary>
/// What this app tells the management app about itself. `--register` (run on every deploy) sends
/// it, so the roles below exist in the registry before new code that uses them goes live.
/// </summary>
public sealed class Manifest : AppManifest
{
    public override string Key => "AZURE-APP-NAME-PLACEHOLDER";
    public override TenancyMode Tenancy => TenancyMode.TENANCY-MODE-PLACEHOLDER;

    // Roles are assigned in the management app and enforced with [Authorize(Roles = "...")].
    // Entra App Roles are ignored in apps that use the registry.
    public override IReadOnlyList<AppRoleDefinition> Roles =>
    [
        new("editor", "Editor"),
        new("viewer", "Viewer"),
    ];
}
