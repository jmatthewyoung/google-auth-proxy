using System.Net;
using System.Text.Json.Nodes;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace GoogleAuthProxy;

public class GoogleAuthProxy
{
    private const string GoogleWellKnown =
        "https://accounts.google.com/.well-known/openid-configuration";
    private const string GoogleAuthEndpoint =
        "https://accounts.google.com/o/oauth2/v2/auth";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleAuthProxy> _logger;

    public GoogleAuthProxy(IHttpClientFactory httpClientFactory, ILogger<GoogleAuthProxy> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // Route 1: Return a modified OIDC discovery document.
    // Identical to Google's, but authorization_endpoint points at our proxy.
    [Function("OidcDiscovery")]
    public async Task<HttpResponseData> OidcDiscovery(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "well-known/openid-configuration")] HttpRequestData req)
    {
        _logger.LogInformation("OidcDiscovery: request received");
        try
        {
            var client = _httpClientFactory.CreateClient();

            _logger.LogInformation("OidcDiscovery: fetching Google discovery doc");
            var json = await client.GetStringAsync(GoogleWellKnown);
            _logger.LogInformation("OidcDiscovery: received {Length} bytes", json.Length);

            var config = JsonNode.Parse(json)!.AsObject();

            // Swap authorization_endpoint to point at our proxy
            var proxyBase = $"{req.Url.Scheme}://{req.Url.Host}";
            if (!req.Url.IsDefaultPort)
                proxyBase += $":{req.Url.Port}";

            var proxiedAuth = $"{proxyBase}/api/auth";
            config["authorization_endpoint"] = proxiedAuth;
            _logger.LogInformation("OidcDiscovery: set authorization_endpoint to {Endpoint}", proxiedAuth);

            var response = req.CreateResponse(HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/json");
            await response.WriteStringAsync(config.ToJsonString());
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OidcDiscovery failed: {Message}", ex.Message);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync($"OidcDiscovery failed: {ex.Message}\n{ex.StackTrace}");
            return err;
        }
    }

    // Route 2: Strip the username param and redirect to real Google auth.
    [Function("GoogleAuthProxy")]
    public async Task<HttpResponseData> Auth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "auth")] HttpRequestData req)
    {
        _logger.LogInformation("GoogleAuthProxy: raw query = {Query}", req.Url.Query);
        try
        {
            // Parse query string without System.Web dependency
            var rawQuery = req.Url.Query.TrimStart('?');

            var filteredParams = rawQuery
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .Where(parts => !parts[0].Equals("username", StringComparison.OrdinalIgnoreCase))
                .Select(parts => string.Join("=", parts))
                .ToList();

            _logger.LogInformation("GoogleAuthProxy: {Count} params after stripping username: {Params}",
                filteredParams.Count, string.Join(", ", filteredParams));

            var googleUrl = $"{GoogleAuthEndpoint}?{string.Join("&", filteredParams)}";
            _logger.LogInformation("GoogleAuthProxy: redirecting to {Url}", googleUrl);

            var response = req.CreateResponse(HttpStatusCode.Redirect);
            response.Headers.Add("Location", googleUrl);
            response.Headers.Add("Cache-Control", "no-store");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GoogleAuthProxy failed: {Message}", ex.Message);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync($"GoogleAuthProxy failed: {ex.Message}\n{ex.StackTrace}");
            return err;
        }
    }
}