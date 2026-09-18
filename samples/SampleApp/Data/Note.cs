using PS.AppPlatform.Data;

namespace SampleApp.Data;

public class Note : Entity
{
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public int? WordCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

