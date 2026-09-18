using Wisdi.AppPlatform.Data;

namespace Wisdi.AppPlatform.Tasks;

/// <summary>
/// Represents a background task in the platform. Full properties are defined in Step 06.
/// </summary>
public class BackgroundTask : Entity
{
    public string? Status { get; set; }
    public string? TaskType { get; set; }
    public DateTime CreatedDate { get; set; }
    public Guid ExecutionManagerId { get; set; }
}
