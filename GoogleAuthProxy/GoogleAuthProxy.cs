using System.Net;
using System.Text.Json;
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
        var client = _httpClientFactory.CreateClient();
        var json = await client.GetStringAsync(GoogleWellKnown);

        var config = JsonNode.Parse(json)!.AsObject();

        // Swap authorization_endpoint to our proxy URL
        var proxyBase = $"{req.Url.Scheme}://{req.Url.Host}";
        if (!req.Url.IsDefaultPort)
            proxyBase += $":{req.Url.Port}";

        config["authorization_endpoint"] = $"{proxyBase}/api/auth";

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(config.ToJsonString());
        return response;
    }

    // Route 2: Strip the username param and redirect to real Google auth.
    [Function("GoogleAuthProxy")]
    public HttpResponseData Auth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "auth")] HttpRequestData req)
    {
        var incomingParams = System.Web.HttpUtility.ParseQueryString(req.Url.Query);

        _logger.LogInformation("Incoming params from Entra: {Params}",
            string.Join(", ", incomingParams.AllKeys.Select(k => $"{k}={incomingParams[k]}")));

        // Remove the param Google rejects
        incomingParams.Remove("username");

        var builder = new UriBuilder(GoogleAuthEndpoint)
        {
            Query = incomingParams.ToString()
        };

        _logger.LogInformation("Forwarding to Google: {Url}", builder.Uri);

        var response = req.CreateResponse(HttpStatusCode.Redirect);
        response.Headers.Add("Location", builder.Uri.ToString());
        response.Headers.Add("Cache-Control", "no-store");
        return response;
    }
}