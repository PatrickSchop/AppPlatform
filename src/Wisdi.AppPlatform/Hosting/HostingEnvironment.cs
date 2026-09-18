using Microsoft.Extensions.Configuration;

namespace PS.AppPlatform.Hosting;

public class HostingEnvironment : IHostingEnvironment
{
    public EnvironmentType EnvironmentType { get; }

    public HostingEnvironment(IConfiguration configuration)
    {
        EnvironmentType = configuration.GetValue<string>("DEV_ENVIRONMENT")?.ToLower() switch
        {
            "development" => EnvironmentType.Development,
            _ => EnvironmentType.Production,
        };
    }
}

