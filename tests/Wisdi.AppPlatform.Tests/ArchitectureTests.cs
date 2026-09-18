using System.Reflection;
using PS.AppPlatform.Data;
using Xunit;

namespace PS.AppPlatform.Tests;

public class ArchitectureTests
{
    private static readonly Assembly Platform = typeof(Entity).Assembly;

    [Fact]
    public void Platform_assembly_declares_no_Function_attributes()
    {
        var offenders = Platform.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.Instance | BindingFlags.Static
                                          | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes()
                         .Any(a => a.GetType().Name == "FunctionAttribute"))
            .Select(m => $"{m.DeclaringType!.FullName}.{m.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            "The platform assembly must not declare [Function] methods; they are invisible "
            + "to worker indexing when shipped in a package. Offenders: "
            + string.Join(", ", offenders));
    }
}

