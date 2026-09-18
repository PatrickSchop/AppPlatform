using Microsoft.Azure.Functions.Worker;
using System.Reflection;

namespace Wisdi.AppPlatform.Auth;

internal static class FunctionContextExtensions
{
    /// <summary>
    /// Gets the target MethodInfo being executed by this function context.
    /// This works by reflection against the entry point defined in the FunctionDefinition.
    /// </summary>
    public static MethodInfo? GetTargetFunctionMethod(this FunctionContext context)
    {
        var definition = context.FunctionDefinition;
        if (definition?.EntryPoint == null)
            return null;

        // EntryPoint format: "namespace.ClassName.MethodName"
        var parts = definition.EntryPoint.Split('.');
        if (parts.Length < 2)
            return null;

        var methodName = parts[^1];
        var className = parts[^2];
        var namespaceName = string.Join(".", parts.Take(parts.Length - 2));

        try
        {
            // Find the type in any loaded assembly
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Namespace == namespaceName && t.Name == className);

            if (type == null)
                return null;

            // Find the method - it could be static or instance
            var method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);

            return method;
        }
        catch
        {
            return null;
        }
    }
}
