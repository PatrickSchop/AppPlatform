namespace Wisdi.AppPlatform.Endpoints;

/// <summary>
/// Response model for background task information.
/// Property names match existing client expectations (lowercase).
/// </summary>
public sealed record BackgroundTaskResponse(
    Guid id,
    string taskType,
    string status,
    string statusMessage,
    int completionPercentage,
    string description,
    bool requiresNotification,
    DateTime createdDate,
    DateTime updatedDate,
    DateTime? startedDate,
    DateTime? completedDate);
