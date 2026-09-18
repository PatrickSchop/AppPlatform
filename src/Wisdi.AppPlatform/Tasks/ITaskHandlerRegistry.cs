namespace Wisdi.AppPlatform.Tasks;

public interface ITaskHandlerRegistry
{
    Type? GetHandlerType(string taskType);
    bool IsRegistered(string taskType);
}
