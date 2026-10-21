using System.Text.Json;
using PS.AppPlatform.Tenancy;
using PS.Management.Api.Contracts;
using Xunit;

namespace PS.Management.Tests;

public class AdminContractsJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ApplicationSummary_SerializesTenancyAsAString_AndCamelCaseNames()
    {
        // The Angular models in WebApp/src/app/services/models.ts depend on exactly this shape.
        var json = JsonSerializer.Serialize(
            new ApplicationSummary(Guid.NewGuid(), "management", "Application management", TenancyMode.Single, 1, 2, false), Web);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Single", root.GetProperty("tenancy").GetString());
        Assert.Equal("management", root.GetProperty("key").GetString());
        Assert.Equal("Application management", root.GetProperty("displayName").GetString());
        Assert.Equal(1, root.GetProperty("tenantCount").GetInt32());
        Assert.Equal(2, root.GetProperty("userCount").GetInt32());
        Assert.False(root.GetProperty("isDisabled").GetBoolean());
    }

    [Fact]
    public void ApplicationDetail_SerializesTenancyAsAString()
    {
        var json = JsonSerializer.Serialize(
            new ApplicationDetail(Guid.NewGuid(), "management", "Application management", TenancyMode.Multi, false, [], []), Web);

        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Multi", doc.RootElement.GetProperty("tenancy").GetString());
    }
}
