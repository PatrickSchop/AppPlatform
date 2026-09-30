using System.Reflection;
using System.Text.RegularExpressions;

namespace PS.AppPlatform.Tenancy;

public static class AppManifestValidator
{
    /// <summary>Validates app key and roles using the same regexes as AppManifest.</summary>
    public static void Validate(string key, IReadOnlyList<AppRoleDefinition> roles, string sourceTypeName)
    {
        var keyRegex = new Regex(@"^[a-z][a-z0-9\-]{2,39}$", RegexOptions.Compiled);
        if (!keyRegex.IsMatch(key))
        {
            throw new InvalidOperationException(
                $"Invalid app key '{key}' in {sourceTypeName}: must be 3-40 lowercase letters, digits, or hyphens.");
        }

        var roleRegex = new Regex(@"^[a-z][a-z0-9.\-]{0,63}$", RegexOptions.Compiled);
        var seenRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var role in roles)
        {
            if (!roleRegex.IsMatch(role.Name))
            {
                throw new InvalidOperationException(
                    $"Invalid role name '{role.Name}' in {sourceTypeName}: must match ^[a-z][a-z0-9.\\-]{{0,63}}$");
            }

            if (!seenRoles.Add(role.Name))
            {
                throw new InvalidOperationException(
                    $"Duplicate role '{role.Name}' in {sourceTypeName} (roles are case-insensitive)");
            }
        }
    }

    /// <summary>Validates registration body key and roles for API registration.</summary>
    public static void ValidateRegistration<T>(string key, T body) where T : class
    {
        IReadOnlyList<AppRoleDefinition> roles = [];

        // Try to get Roles property
        var rolesProperty = typeof(T).GetProperty("Roles");
        if (rolesProperty != null)
        {
            var rolesValue = rolesProperty.GetValue(body);
            if (rolesValue is IReadOnlyList<AppRoleDefinition> rolesList)
            {
                roles = rolesList;
            }
        }

        Validate(key, roles, "registration");
    }
}

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
        AppManifestValidator.Validate(Key, Roles, GetType().Name);
    }
}
