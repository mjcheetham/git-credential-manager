using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitCredentialManager.UsageSurvey;
using GitCredentialManager.Tests.Objects;
using Xunit;

namespace GitCredentialManager.Tests.UsageSurvey;

public class AppInsightsUploaderTests
{
    private const string SampleConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000001;" +
        "IngestionEndpoint=https://westeurope-1.in.applicationinsights.azure.com/";

    private const string SampleEvent =
        "{\"event\":\"get\",\"event_version\":1,\"ts\":\"2026-06-11T12:00:00Z\"," +
        "\"install_id\":\"abc\",\"gcm_version\":\"2.8.0.0\",\"os\":\"macos\"," +
        "\"os_version\":\"14.5\",\"arch\":\"arm64\",\"provider\":\"github\"," +
        "\"auth_method\":\"oauth\",\"from_cache\":false}";

    private static (AppInsightsUploader uploader, TestHttpMessageHandler http, Uri trackUri) Build()
    {
        var http = new TestHttpMessageHandler { ThrowOnUnexpectedRequest = true };
        var trackUri = new Uri("https://westeurope-1.in.applicationinsights.azure.com/v2/track");
        var client = new HttpClient(http);
        var uploader = new AppInsightsUploader(
            client,
            new NullTrace(),
            trackUri,
            instrumentationKey: "00000000-0000-0000-0000-000000000001");
        return (uploader, http, trackUri);
    }

    [Theory]
    [InlineData(
        "InstrumentationKey=00000000-0000-0000-0000-000000000001;IngestionEndpoint=https://example.com/",
        true)]
    [InlineData(
        "IngestionEndpoint=https://example.com/;InstrumentationKey=abc", // swapped order
        true)]
    [InlineData(
        "InstrumentationKey=abc;IngestionEndpoint=https://example.com", // no trailing slash
        true)]
    [InlineData("", false)]
    [InlineData("InstrumentationKey=onlykey", false)]                    // no endpoint
    [InlineData("IngestionEndpoint=https://example.com/", false)]        // no key
    [InlineData("InstrumentationKey=k;IngestionEndpoint=not-a-url", false)]
    public void TryParseConnectionString_Accepts_Or_Rejects(string conn, bool expectedOk)
    {
        bool ok = AppInsightsUploader.TryParseConnectionString(conn, out string ikey, out Uri ep);
        Assert.Equal(expectedOk, ok);
        if (expectedOk)
        {
            Assert.False(string.IsNullOrEmpty(ikey));
            Assert.NotNull(ep);
            Assert.EndsWith("/", ep.AbsoluteUri, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UploadAsync_Posts_AppInsights_Envelope()
    {
        var (uploader, http, trackUri) = Build();

        HttpRequestMessage captured = null;
        string capturedBody = null;
        http.Setup(HttpMethod.Post, trackUri, async request =>
        {
            captured = request;
            capturedBody = await request.Content.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        bool ok = await uploader.UploadAsync(SampleEvent, CancellationToken.None);

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Equal("application/json", captured.Content.Headers.ContentType.MediaType);

        using var doc = JsonDocument.Parse(capturedBody);
        JsonElement root = doc.RootElement;

        Assert.Equal("Microsoft.ApplicationInsights.Event", root.GetProperty("name").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000001", root.GetProperty("iKey").GetString());

        JsonElement data = root.GetProperty("data");
        Assert.Equal("EventData", data.GetProperty("baseType").GetString());

        JsonElement baseData = data.GetProperty("baseData");
        Assert.Equal(2, baseData.GetProperty("ver").GetInt32());
        Assert.Equal("gcm.get", baseData.GetProperty("name").GetString());

        JsonElement props = baseData.GetProperty("properties");
        Assert.Equal("get",            props.GetProperty("event").GetString());
        Assert.Equal("1",              props.GetProperty("event_version").GetString());
        Assert.Equal("abc",            props.GetProperty("install_id").GetString());
        Assert.Equal("macos",          props.GetProperty("os").GetString());
        Assert.Equal("14.5",           props.GetProperty("os_version").GetString());
        Assert.Equal("arm64",          props.GetProperty("arch").GetString());
        Assert.Equal("github",         props.GetProperty("provider").GetString());
        Assert.Equal("oauth",          props.GetProperty("auth_method").GetString());
        Assert.Equal("false",          props.GetProperty("from_cache").GetString());
    }

    [Fact]
    public async Task UploadAsync_5xx_Returns_False_For_Retry()
    {
        var (uploader, http, trackUri) = Build();
        http.Setup(HttpMethod.Post, trackUri, HttpStatusCode.InternalServerError);

        bool ok = await uploader.UploadAsync(SampleEvent, CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task UploadAsync_4xx_Returns_True_To_Drop()
    {
        var (uploader, http, trackUri) = Build();
        http.Setup(HttpMethod.Post, trackUri, HttpStatusCode.BadRequest);

        // 4xx => non-retriable. Returning true tells the dispatcher to
        // archive the event as "shipped" and stop trying.
        bool ok = await uploader.UploadAsync(SampleEvent, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public async Task UploadAsync_Network_Error_Returns_False()
    {
        var (uploader, http, _) = Build();
        http.SimulateNoNetwork = true;

        bool ok = await uploader.UploadAsync(SampleEvent, CancellationToken.None);
        Assert.False(ok);
    }

    [Fact]
    public async Task UploadAsync_Malformed_Line_Drops_Without_Network_Call()
    {
        var (uploader, http, _) = Build();

        bool ok = await uploader.UploadAsync("{not valid json", CancellationToken.None);
        Assert.True(ok); // drop
        http.AssertNoRequests();
    }

    [Fact]
    public async Task UploadAsync_Empty_Line_Is_Noop()
    {
        var (uploader, http, _) = Build();

        Assert.True(await uploader.UploadAsync("", CancellationToken.None));
        Assert.True(await uploader.UploadAsync("   ", CancellationToken.None));
        http.AssertNoRequests();
    }

    [Fact]
    public void TryCreate_No_Endpoint_Configured_Returns_False()
    {
        var ctx = new TestCommandContext();
        // DefaultEndpoint is empty in source; no env override set.
        bool ok = AppInsightsUploader.TryCreate(ctx, out AppInsightsUploader uploader);
        Assert.False(ok);
        Assert.Null(uploader);
    }

    [Fact]
    public void TryCreate_With_EnvVar_Override_Returns_True()
    {
        var ctx = new TestCommandContext();
        ctx.Environment.Variables[Constants.EnvironmentVariables.GcmUsageSurveyEndpoint] =
            SampleConnectionString;

        bool ok = AppInsightsUploader.TryCreate(ctx, out AppInsightsUploader uploader);
        Assert.True(ok);
        Assert.NotNull(uploader);
    }

    [Fact]
    public void TryCreate_Malformed_EnvVar_Returns_False()
    {
        var ctx = new TestCommandContext();
        ctx.Environment.Variables[Constants.EnvironmentVariables.GcmUsageSurveyEndpoint] =
            "this-is-not-a-connection-string";

        bool ok = AppInsightsUploader.TryCreate(ctx, out AppInsightsUploader uploader);
        Assert.False(ok);
        Assert.Null(uploader);
    }
}
