using Microsoft.Extensions.DependencyInjection;

namespace Wisdi.AppPlatform.Tasks;

/// <summary>
/// Implementation of IBackgroundTaskCollection that collects background task handler registrations.
/// </summary>
public class BackgroundTaskCollection : IBackgroundTaskCollection
{
    private readonly Dictionary<string, Type> _registrations = new(StringComparer.OrdinalIgnoreCase);

    public void AddBackgroundTask(string taskName, Type handlerType)
    {
        if (string.IsNullOrEmpty(taskName))
        {
            throw new ArgumentNullException(nameof(taskName));
        }

        if (handlerType == null)
        {
            throw new ArgumentNullException(nameof(handlerType));
        }

        if (_registrations.TryGetValue(taskName, out var existingType) && existingType != handlerType)
        {
            throw new InvalidOperationException($"Task '{taskName}' is already registered to a different handler type. Previous: {existingType.Name}, New: {handlerType.Name}");
        }

        _registrations[taskName] = handlerType;
    }

    public void AddBackgroundTask<T>(string taskName) where T : class
    {
        AddBackgroundTask(taskName, typeof(T));
    }

    public IReadOnlyDictionary<string, Type> GetRegistrations()
    {
        return _registrations.AsReadOnly();
    }

    public void RegisterBackgroundTaskHandlers(IServiceCollection services)
    {
        foreach (var registration in _registrations.Values.Distinct())
        {
            services.AddTransient(registration);
        }

        services.AddSingleton<ITaskHandlerRegistry>(sp =>
        {
            var registry = new TaskHandlerRegistry();
            foreach (var (taskName, handlerType) in _registrations)
            {
                registry.RegisterHandler(taskName, handlerType);
            }
            return registry;
        });
    }
}
