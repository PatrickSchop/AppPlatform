using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PS.AppPlatform.Auth;
using PS.AppPlatform.Tenancy;
using PS.Management.Registry;

namespace PS.Management.Api;

public sealed record RegistrationBody(
    string Key,
    string DisplayName,
    TenancyMode Tenancy,
    IReadOnlyList<AppRoleDefinition> Roles,
    Guid? ServicePrincipalId);

public sealed class RegistryEndpoints(
    RegistryService registryService,
    MembershipQuery membershipQuery,
    IOptions<RegistryOptions> options,
    ILogger<RegistryEndpoints> logger)
{
    public async Task<IActionResult> RegisterAsync(HttpRequest req, string key, CancellationToken ct)
    {
        // Check authorization
        var authError = RegistryCallers.CheckRegister(req.HttpContext.User, options.Value, logger);
        if (authError != null)
        {
            return new ObjectResult(new { error = authError }) { StatusCode = 403 };
        }

        // Deserialize body
        RegistrationBody? body;
        try
        {
            body = await JsonSerializer.DeserializeAsync<RegistrationBody>(
                req.Body, JsonSerializerOptions.Web, ct);
        }
        catch
        {
            return new BadRequestObjectResult(new { error = "invalid_body" });
        }

        if (body == null)
            return new BadRequestObjectResult(new { error = "invalid_body" });

        // Validate key matches route
        if (body.Key != key)
            return new BadRequestObjectResult(new { error = "key_mismatch" });

        // Validate with AppManifestValidator
        try
        {
            AppManifestValidator.ValidateRegistration(key, body);
        }
        catch (InvalidOperationException ex)
        {
            return new BadRequestObjectResult(new { error = ex.Message });
        }

        // Call service
        var registration = new ApplicationRegistration(
            body.Key,
            body.DisplayName,
            body.Tenancy,
            body.Roles,
            body.ServicePrincipalId);

        var result = await registryService.UpsertApplicationAsync(registration, ct);

        // Set cache control
        req.HttpContext.Response.Headers["Cache-Control"] = "no-store";

        return new OkObjectResult(result);
    }

    public async Task<IActionResult> GetMembershipsAsync(HttpRequest req, string key, string oid, CancellationToken ct)
    {
        // Look up application by key
        var app = await registryService.GetApplicationByKeyAsync(key, ct);
        if (app == null)
            return new NotFoundResult();

        // Check authorization
        var authError = RegistryCallers.CheckMembershipLookup(
            req.HttpContext.User, app.ServicePrincipalId, options.Value, logger);
        if (authError != null)
        {
            return new ObjectResult(new { error = authError }) { StatusCode = 403 };
        }

        // Read tid from query string
        if (!req.Query.TryGetValue("tid", out var tidValues) || tidValues.Count == 0)
            return new BadRequestObjectResult(new { error = "tid_required" });

        var tid = tidValues[0];
        if (string.IsNullOrEmpty(tid))
            return new BadRequestObjectResult(new { error = "tid_required" });

        // Get memberships
        var memberships = await membershipQuery.GetAsync(key, new IdentityKey(oid, tid), ct);
        if (memberships == null)
            return new NotFoundResult();

        // Set cache control
        req.HttpContext.Response.Headers["Cache-Control"] = "no-store";

        return new OkObjectResult(memberships);
    }
}
