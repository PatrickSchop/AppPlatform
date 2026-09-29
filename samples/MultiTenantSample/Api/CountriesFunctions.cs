using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using MultiTenantSample.Api;

namespace MultiTenantSample.Generated;

public class CountriesFunctions(CountriesEndpoints inner)
{
    [Function("GetCountries")]
    [Authorize]
    public Task<IActionResult> GetCountries(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/countries")] HttpRequest req)
        => inner.GetAllAsync(req, req.HttpContext.RequestAborted);
}
