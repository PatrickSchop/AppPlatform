using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SampleApp.Data;
using SampleApp.Tasks;
using Wisdi.AppPlatform.Data;
using Wisdi.AppPlatform.Tasks;

namespace SampleApp.Api;

public class NotesEndpoints(IDbContextFactory<AppDbContext> factory, IBackgroundTaskService service)
{
    public async Task<IActionResult> GetAllAsync(HttpRequest req, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var notes = await db.Notes.OrderByDescending(x => x.CreatedUtc).ToListAsync(ct);
        return new OkObjectResult(notes);
    }

    public async Task<IActionResult> CreateAsync(HttpRequest req, CancellationToken ct)
    {
        using var reader = new StreamReader(req.Body);
        var json = await reader.ReadToEndAsync(ct);
        var note = System.Text.Json.JsonSerializer.Deserialize<Note>(json);
        if (note == null)
            return new BadRequestResult();

        await using var db = await factory.CreateDbContextAsync(ct);
        db.Notes.Add(note);
        await db.SaveChangesAsync(ct);

        return new OkObjectResult(note);
    }

    public async Task<IActionResult> StartWordCountAsync(HttpRequest req, CancellationToken ct)
    {
        var taskId = await service.CreateTaskAsync("wordcount", new WordCountTaskData(), "Counting words", requiresNotification: true, ct);
        return new OkObjectResult(new { taskId });
    }
}
