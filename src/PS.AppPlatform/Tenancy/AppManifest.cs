using System.Text.RegularExpressions;

namespace PS.AppPlatform.Tenancy;

public sealed record AppRoleDefinition(
    string Name,
    string DisplayName,
    string? Description = null);

/// <summary>
/// Declares the application to the registry. Exactly one non-abstract subclass per app,
/// discovered across PlatformAssemblies like ServiceBuilder (public parameterless ctor).
/// </summary>
public abstract class AppManifest
{
    /// <summary>Stable registry key: lowercase letters, digits, '-'; 3-40 chars.</summary>
    public abstract string Key { get; }

    public virtual string DisplayName => Key;

    public abstract TenancyMode Tenancy { get; }

    public virtual IReadOnlyList<AppRoleDefinition> Roles => [];

    internal void Validate()
    {
        var keyRegex = new Regex(@"^[a-z][a-z0-9\-]{2,39}$", RegexOptions.Compiled);
        if (!keyRegex.IsMatch(Key))
        {
            throw new InvalidOperationException(
                $"Invalid app key '{Key}' in {GetType().Name}: must be 3-40 lowercase letters, digits, or hyphens.");
        }

        var roleRegex = new Regex(@"^[a-z][a-z0-9.\-]{0,63}$", RegexOptions.Compiled);
        var seenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in Roles)
        {
            if (!roleRegex.IsMatch(role.Name))
            {
                throw new InvalidOperationException(
                    $"Invalid role name '{role.Name}' in {GetType().Name}: must match ^[a-z][a-z0-9.\\-]{{0,63}}$");
            }

            if (!seenRoles.Add(role.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate role '{role.Name}' in {GetType().Name} (roles are case-insensitive)");
            }
        }
    }
}
