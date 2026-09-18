using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using SampleApp.Api;

namespace SampleApp.Generated;

public class NotesFunctions(NotesEndpoints inner)
{
    [Function("GetNotes")]
    [Authorize]
    public Task<IActionResult> GetNotes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/notes")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);

    [Function("CreateNote")]
    [Authorize]
    public Task<IActionResult> CreateNote(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/notes")] HttpRequest req)
        => inner.CreateAsync(req, req.HttpContext.RequestAborted);

    [Function("StartWordCount")]
    [Authorize]
    public Task<IActionResult> StartWordCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/notes/wordcount")] HttpRequest req)
        => inner.StartWordCountAsync(req, req.HttpContext.RequestAborted);
}
