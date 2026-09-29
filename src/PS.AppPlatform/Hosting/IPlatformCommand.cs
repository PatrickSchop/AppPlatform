namespace PS.AppPlatform.Hosting;

/// <summary>A one-shot CLI command run instead of the Functions host, e.g. --migrate.</summary>
public interface IPlatformCommand
{
    /// <summary>The flag that selects this command, without dashes, e.g. "migrate".</summary>
    string Name { get; }

    /// <summary>Returns a process exit code: 0 success, non-zero failure.</summary>
    Task<int> RunAsync(PlatformCommandArgs args, IServiceProvider services, CancellationToken ct);
}

public sealed record PlatformCommandArgs(string[] Raw)
{
    /// <summary>Value after --name or -name, or null.</summary>
    public string? Get(string name)
    {
        for (int i = 0; i < Raw.Length - 1; i++)
        {
            if ((Raw[i] == $"--{name}" || Raw[i] == $"-{name}") && i + 1 < Raw.Length)
            {
                return Raw[i + 1];
            }
        }

        return null;
    }

    /// <summary>Value after --name or -name, throws if missing.</summary>
    public string Require(string name)
    {
        var value = Get(name);
        if (value == null)
        {
            throw new ArgumentException($"Required flag --{name} not provided.");
        }

        return value;
    }
}
