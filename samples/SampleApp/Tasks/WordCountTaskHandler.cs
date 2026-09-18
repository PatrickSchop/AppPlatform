using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SampleApp.Data;
using PS.AppPlatform.Data;
using PS.AppPlatform.Tasks;

namespace SampleApp.Tasks;

public class WordCountTaskHandler(IDbContextFactory<AppDbContext> factory)
    : ITaskHandler<WordCountTaskData>
{
    public async Task HandleAsync(BackgroundTask task, WordCountTaskData data,
                                  TaskHandlerContext context, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var notes = await db.Notes.ToListAsync(ct);

        for (var i = 0; i < notes.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            notes[i].WordCount = notes[i].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            await Task.Delay(500, ct);
            await context.UpdateProgressAsync((i + 1) * 100 / Math.Max(notes.Count, 1));
        }

        await db.SaveChangesAsync(ct);
        await context.CompleteAsync();
    }
}

public sealed record WordCountTaskData(bool Recount = true);

