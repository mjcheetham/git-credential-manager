using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitCredentialManager.UsageSurvey;

/// <summary>
/// Ships usage survey events to an Azure Application Insights resource via its
/// public HTTPS ingestion endpoint. Uses the raw <c>/v2/track</c> REST API
/// rather than the Microsoft.ApplicationInsights SDK so we don't drag a heavy
/// dependency into GCM for what is essentially one POST per event.
/// </summary>
/// <remarks>
/// <para>
/// The connection string is read from <c>GCM_USAGE_SURVEY_ENDPOINT</c> if set,
/// otherwise from <see cref="Constants.UsageSurvey.DefaultEndpoint"/>. The
/// connection-string grammar is the standard one documented by Azure Monitor:
/// a <c>;</c>-separated list of <c>Key=Value</c> pairs, of which we recognise
/// <c>InstrumentationKey</c> and <c>IngestionEndpoint</c>.
/// </para>
/// <para>
/// Events are emitted as a single Application Insights <c>EventData</c>
/// envelope whose <c>name</c> is <c>gcm.&lt;event&gt;</c> (e.g.
/// <c>gcm.get</c>) and whose <c>properties</c> dictionary is the flattened
/// <see cref="UsageSurveyEvent"/>. Custom-dimension values must be strings, so
/// numeric and boolean fields are stringified at the boundary.
/// </para>
/// </remarks>
public sealed class AppInsightsUploader : IUsageSurveyUploader
{
    // Documented Application Insights envelope schema names.
    private const string EnvelopeTypeName = "Microsoft.ApplicationInsights.Event";
    private const string DataBaseType = "EventData";
    private const int DataBaseDataVersion = 2;

    private readonly HttpClient _httpClient;
    private readonly ITrace _trace;
    private readonly Uri _trackUri;
    private readonly string _instrumentationKey;

    public AppInsightsUploader(HttpClient httpClient, ITrace trace, Uri trackUri, string instrumentationKey)
    {
        EnsureArgument.NotNull(httpClient, nameof(httpClient));
        EnsureArgument.NotNull(trace, nameof(trace));
        EnsureArgument.NotNull(trackUri, nameof(trackUri));
        EnsureArgument.NotNullOrWhiteSpace(instrumentationKey, nameof(instrumentationKey));

        _httpClient = httpClient;
        _trace = trace;
        _trackUri = trackUri;
        _instrumentationKey = instrumentationKey;
    }

    /// <summary>
    /// Build an <see cref="AppInsightsUploader"/> if a connection string is
    /// configured (env var or default constant); return false otherwise so the
    /// caller can fall back to <see cref="StubFileUploader"/>.
    /// </summary>
    public static bool TryCreate(ICommandContext context, out AppInsightsUploader uploader)
    {
        EnsureArgument.NotNull(context, nameof(context));
        uploader = null;

        if (!context.Environment.Variables.TryGetValue(
                Constants.EnvironmentVariables.GcmUsageSurveyEndpoint,
                out string connectionString) ||
            string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = Constants.UsageSurvey.DefaultEndpoint;
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        if (!TryParseConnectionString(connectionString, out string ikey, out Uri ingestionEndpoint))
        {
            context.Trace.WriteLine("Usage survey: GCM_USAGE_SURVEY_ENDPOINT is malformed; falling back to stub uploader.");
            return false;
        }

        var trackUri = new Uri(ingestionEndpoint, "v2/track");
        uploader = new AppInsightsUploader(
            context.HttpClientFactory.CreateClient(),
            context.Trace,
            trackUri,
            ikey);
        return true;
    }

    public async Task<bool> UploadAsync(string jsonLine, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return true;
        }

        string body;
        try
        {
            body = BuildEnvelope(jsonLine);
        }
        catch (Exception ex)
        {
            // A malformed event line is not retriable; drop it so we don't
            // loop forever trying to ship the same bad file.
            _trace.WriteLine($"AppInsightsUploader: failed to build envelope: {ex.Message}");
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _trackUri)
            {
                Content = new StringContent(body, Encoding.UTF8, Constants.Http.MimeTypeJson),
            };

            using HttpResponseMessage response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            int status = (int)response.StatusCode;

            if (status >= 200 && status < 300)
            {
                return true;
            }

            // Application Insights uses 206 Partial Content when only some
            // items in a batch were accepted; we only ever send one item, so
            // treat anything outside 2xx-success as a failure.
            if (status >= 500)
            {
                // Server side trouble — retriable.
                _trace.WriteLine($"AppInsightsUploader: {status} from ingestion endpoint; will retry.");
                return false;
            }

            // 4xx — request was bad. Don't retry forever; drop it.
            _trace.WriteLine($"AppInsightsUploader: {status} from ingestion endpoint; dropping event.");
            return true;
        }
        catch (TaskCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Network exception, DNS failure, TLS error — all retriable.
            _trace.WriteLine($"AppInsightsUploader: send failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Parse an Application Insights connection string of the form
    /// <c>"InstrumentationKey=...;IngestionEndpoint=https://...;</c>...".
    /// </summary>
    internal static bool TryParseConnectionString(string connectionString, out string instrumentationKey, out Uri ingestionEndpoint)
    {
        instrumentationKey = null;
        ingestionEndpoint = null;

        if (string.IsNullOrWhiteSpace(connectionString)) return false;

        foreach (string part in connectionString.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0) continue;

            string key = part.Substring(0, eq).Trim();
            string val = part.Substring(eq + 1).Trim();

            if (StringComparer.OrdinalIgnoreCase.Equals(key, "InstrumentationKey"))
            {
                instrumentationKey = val;
            }
            else if (StringComparer.OrdinalIgnoreCase.Equals(key, "IngestionEndpoint"))
            {
                if (!Uri.TryCreate(val, UriKind.Absolute, out ingestionEndpoint))
                {
                    return false;
                }
                if (!ingestionEndpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal))
                {
                    ingestionEndpoint = new Uri(ingestionEndpoint.AbsoluteUri + "/");
                }
            }
        }

        return !string.IsNullOrWhiteSpace(instrumentationKey) && ingestionEndpoint != null;
    }

    /// <summary>
    /// Wrap a single <see cref="UsageSurveyEvent"/>-shaped JSON line in the
    /// Application Insights <c>EventData</c> envelope expected by
    /// <c>/v2/track</c>. Exposed internal for testing.
    /// </summary>
    internal string BuildEnvelope(string eventJsonLine)
    {
        using JsonDocument doc = JsonDocument.Parse(eventJsonLine);
        JsonElement evt = doc.RootElement;

        string eventName = evt.TryGetProperty("event", out JsonElement nameProp) && nameProp.ValueKind == JsonValueKind.String
            ? "gcm." + nameProp.GetString()
            : "gcm.unknown";

        var properties = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty prop in evt.EnumerateObject())
        {
            properties[prop.Name] = JsonValueToString(prop.Value);
        }

        using var ms = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("name", EnvelopeTypeName);
            w.WriteString("time", DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
            w.WriteString("iKey", _instrumentationKey);

            w.WriteStartObject("data");
            w.WriteString("baseType", DataBaseType);

            w.WriteStartObject("baseData");
            w.WriteNumber("ver", DataBaseDataVersion);
            w.WriteString("name", eventName);

            w.WriteStartObject("properties");
            foreach (KeyValuePair<string, string> kvp in properties)
            {
                w.WriteString(kvp.Key, kvp.Value);
            }
            w.WriteEndObject(); // properties

            w.WriteEndObject(); // baseData
            w.WriteEndObject(); // data
            w.WriteEndObject(); // envelope
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string JsonValueToString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Null   => string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            _ => value.GetRawText(),
        };
    }
}
