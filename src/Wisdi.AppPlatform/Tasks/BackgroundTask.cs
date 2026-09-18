using Wisdi.AppPlatform.Data;

namespace Wisdi.AppPlatform.Tasks;

public class BackgroundTask : Entity
{
    public string TaskType { get; set; } = string.Empty;
    public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.New;
    public string StatusMessage { get; set; } = string.Empty;
    public int CompletionPercentage { get; set; } = 0;
    public string Description { get; set; } = string.Empty;
    public bool RequiresNotification { get; set; } = false;
    public string TaskData { get; set; } = string.Empty;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? StartedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public Guid? ExecutionManagerId { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
}
