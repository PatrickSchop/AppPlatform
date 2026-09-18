namespace PS.AppPlatform.Tasks;

/// <summary>
/// Represents the lifecycle status of a background task.
/// Status values use a bitmask scheme: concrete values embed flags that can be tested with bitwise AND.
/// Running (9) = 8|1 = ExecutingFlag | marker; Failed (33) = 32|1 = CompletedFlag | marker.
/// This allows UpdateStatusAsync to check (Status &amp; ExecutingFlag) to test if a task is actively running
/// without explicitly listing every possible "running" state.
/// </summary>
public enum BackgroundTaskStatus
{
    New = 1,
    Resumed = 2,
    NotStarted = 8,
    Running = 9,
    Paused = 16,
    Completed = 32,
    Failed = 33,

    ExecutingFlag = 8,
    CompletedFlag = 32
}

