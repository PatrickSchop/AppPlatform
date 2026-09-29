using Microsoft.Azure.Functions.Worker;
using System.Collections.Concurrent;
using System.Reflection;

namespace PS.AppPlatform.Auth;

internal static class FunctionContextExtensions
{
    /// <summary>
    /// Gets the target MethodInfo being executed by this function context.
    /// This works by reflection against the entry point defined in the FunctionDefinition.
    /// </summary>
    public static MethodInfo? GetTargetFunctionMethod(this FunctionContext context)
    {
        var entryPoint = context.FunctionDefinition?.EntryPoint;
        if (entryPoint == null)
            return null;

        // The lookup scans every loaded assembly, so it runs once per entry point, not per request.
        return MethodCache.GetOrAdd(entryPoint, FindMethod);
    }

    private static readonly ConcurrentDictionary<string, MethodInfo?> MethodCache = new(StringComparer.Ordinal);

    private static MethodInfo? FindMethod(string entryPoint)
    {
        // EntryPoint format: "namespace.ClassName.MethodName"
        var parts = entryPoint.Split('.');
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

            // Shim methods are instance methods on primary-constructor classes, so Instance
            // is required here; without it GetMethod returns null and [AllowAnonymous] is
            // silently missed, making every anonymous endpoint fail closed.
            var method = type.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.IgnoreCase);

            return method;
        }
        catch
        {
            return null;
        }
    }
}

