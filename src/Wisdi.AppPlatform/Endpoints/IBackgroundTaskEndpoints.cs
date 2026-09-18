using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Wisdi.AppPlatform.Endpoints;

/// <summary>
/// Interface for background task endpoints.
/// Implementations handle task queries, creation, and execution management.
/// </summary>
public interface IBackgroundTaskEndpoints
{
    /// <summary>
    /// Get tasks that require notification: either running/pending or completed within the notification window.
    /// GET /api/tasks/notifications
    /// </summary>
    Task<IActionResult> GetNotificationsAsync(HttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get all background tasks.
    /// GET /api/tasks
    /// </summary>
    Task<IActionResult> GetAllAsync(HttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get a specific background task by ID.
    /// GET /api/tasks/{id}
    /// </summary>
    Task<IActionResult> GetByIdAsync(HttpRequest request, string id, CancellationToken ct = default);

    /// <summary>
    /// Create a new background task.
    /// POST /api/tasks
    /// </summary>
    Task<IActionResult> CreateAsync(HttpRequest request, CancellationToken ct = default);

    /// <summary>
    /// Manually trigger task checking and execution.
    /// POST /api/tasks/check
    /// </summary>
    Task<IActionResult> CheckAsync(HttpRequest request, CancellationToken ct = default);
}
