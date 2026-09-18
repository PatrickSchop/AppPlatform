namespace Wisdi.AppPlatform.Tasks;

public class TaskHandlerRegistry : ITaskHandlerRegistry
{
    private readonly Dictionary<string, Type> _handlers = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterHandler<T>(string taskName) where T : class
    {
        RegisterHandler(taskName, typeof(T));
    }

    public void RegisterHandler(string taskName, Type handlerType)
    {
        _handlers[taskName] = handlerType;
    }

    public Type? GetHandlerType(string taskName)
    {
        return _handlers.TryGetValue(taskName, out var handlerType) ? handlerType : null;
    }
}
