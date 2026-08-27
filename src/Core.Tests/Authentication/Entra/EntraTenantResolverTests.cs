using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using GitCredentialManager.Authentication.Entra;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.Authentication.Entra;

public class EntraTenantResolverTests
{
    [Fact]
    public async Task EntraTenantResolver_LookupAsync_Valid_ReturnsTenantInfoOrCached()
    {
        var tenantId = Guid.NewGuid();
        const string lookupName = "contoso.com";

        string expectedAuthority = $"https://login.microsoftonline.com/{tenantId:D}/v2.0";
        string authEndpoint = $"https://login.microsoftonline.com/{tenantId:D}/oauth2/v2.0/authorize";
        Uri expectedRequestUri = new($"https://login.microsoftonline.com/{lookupName}/v2.0/.well-known/openid-configuration");

        string oidcJson =
            $$"""
            {
              "issuer": "{{expectedAuthority}}",
              "authorization_endpoint": "{{authEndpoint}}"
            }
            """;

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(oidcJson)
        };
        var httpHandler = new TestHttpMessageHandler {ThrowOnUnexpectedRequest = true};
        httpHandler.Setup(HttpMethod.Get, expectedRequestUri, httpResponse);


        var context = new TestCommandContext
        {
            HttpClientFactory =
            {
                MessageHandler = httpHandler
            }
        };

        var resolver = new EntraTenantResolver(context.HttpClientFactory);

        EntraTenant result1 = await resolver.LookupAsync(lookupName);
        Assert.Equal(tenantId, result1.Id);
        Assert.Equal(expectedAuthority, result1.Authority);
        httpHandler.AssertRequest(HttpMethod.Get, expectedRequestUri, 1);

        // Validate repeat lookups for the same tenant name are cached and do not result in additional HTTP requests
        EntraTenant result2 = await resolver.LookupAsync(lookupName);
        Assert.Equal(tenantId, result2.Id);
        Assert.Equal(expectedAuthority, result2.Authority);
        httpHandler.AssertRequest(HttpMethod.Get, expectedRequestUri, 1);
    }

    [Fact]
    public async Task EntraTenantResolver_LookupAsync_Unknown_ReturnsNull()
    {
        const string lookupName = "does-not-exist.example.com";

        Uri expectedRequestUri = new($"https://login.microsoftonline.com/{lookupName}/v2.0/.well-known/openid-configuration");

        string errorJson =
            $$"""
            {
              "error": "invalid_tenant",
              "error_description": "AADSTS90002: Tenant '{{lookupName}}' not found.",
              "error_codes": [90002],
              "timestamp": "2026-08-27 08:57:13Z",
              "trace_id": "TRACE-ID",
              "correlation_id": "CORRELATION-ID",
              "error_uri": "https://login.microsoftonline.com/error?code=90002"
            }
            """;

        var httpResponse = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(errorJson)
        };
        var httpHandler = new TestHttpMessageHandler {ThrowOnUnexpectedRequest = true};
        httpHandler.Setup(HttpMethod.Get, expectedRequestUri, httpResponse);

        var context = new TestCommandContext
        {
            HttpClientFactory =
            {
                MessageHandler = httpHandler
            }
        };

        var resolver = new EntraTenantResolver(context.HttpClientFactory);

        EntraTenant actual = await resolver.LookupAsync(lookupName);

        Assert.Null(actual);
        httpHandler.AssertRequest(HttpMethod.Get, expectedRequestUri, 1);
    }
}
