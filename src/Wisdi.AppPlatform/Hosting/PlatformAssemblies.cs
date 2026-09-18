using System.Reflection;

namespace Wisdi.AppPlatform.Hosting;

/// <summary>
/// The set of assemblies scanned for ServiceBuilder modules and Entity subclasses.
/// Always contains the platform assembly and the entry assembly; apps add extras.
/// </summary>
public sealed class PlatformAssemblies
{
    private readonly List<Assembly> _assemblies = new();

    public PlatformAssemblies()
    {
        Add(typeof(PlatformAssemblies).Assembly);
        var entry = Assembly.GetEntryAssembly();
        if (entry is not null) Add(entry);
    }

    public PlatformAssemblies Add(Assembly assembly)
    {
        if (!_assemblies.Contains(assembly)) _assemblies.Add(assembly);
        return this;
    }

    public PlatformAssemblies AddContaining<T>() => Add(typeof(T).Assembly);

    public IReadOnlyList<Assembly> All => _assemblies;
}
