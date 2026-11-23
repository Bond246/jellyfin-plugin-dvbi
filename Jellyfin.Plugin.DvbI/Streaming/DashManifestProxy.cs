using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.Streaming;

/// <summary>
/// Serves rewritten DASH manifests (see <see cref="DashManifestRewriter"/>) to ffmpeg through the plugin
/// endpoint <c>/DvbI/Dash/{token}/manifest.mpd</c>. ffmpeg reloads live manifests periodically, so every
/// request fetches the current upstream manifest. Segments are loaded by ffmpeg directly from the CDN.
/// Tokens are random and only map to URLs the plugin registered itself, so the anonymous endpoint
/// cannot be used to fetch arbitrary URLs.
/// </summary>
public sealed class DashManifestProxy : IStreamPreparer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(2);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServerApplicationHost _appHost;
    private readonly ILogger<DashManifestProxy> _logger;
    private readonly ConcurrentDictionary<string, Registration> _streams = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DashManifestProxy"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="appHost">Server host, for the loopback URL.</param>
    /// <param name="logger">Logger.</param>
    public DashManifestProxy(IHttpClientFactory httpClientFactory, IServerApplicationHost appHost, ILogger<DashManifestProxy> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appHost = appHost;
        _logger = logger;
    }

    /// <summary>
    /// Gets the number of registered streams.
    /// </summary>
    internal int Count => _streams.Count;

    /// <inheritdoc />
    PreparedStream IStreamPreparer.Prepare(DeliveryKind delivery, Uri url, string userAgent)
    {
        var enabled = Plugin.Instance?.Configuration.EnableDashRewrite ?? true;
        if (delivery != DeliveryKind.Dash || !enabled)
        {
            return new PreparedStream(url, false, static () => { });
        }

        RemoveIdle();
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _streams[token] = new Registration(url, userAgent) { LastAccessUtc = DateTime.UtcNow };
        var local = new Uri(_appHost.GetApiUrlForLocalAccess(IPAddress.Loopback, false).TrimEnd('/') + "/DvbI/Dash/" + token + "/manifest.mpd");
        _logger.LogDebug("DASH manifest proxy registered for {Url}", url);
        return new PreparedStream(local, true, () => _streams.TryRemove(token, out _));
    }

    /// <summary>
    /// Loads and rewrites the manifest of a registered stream.
    /// </summary>
    /// <param name="token">Stream token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rewritten manifest, or null if the token is unknown.</returns>
    /// <exception cref="HttpRequestException">Upstream request failed.</exception>
    internal async Task<string?> GetManifestAsync(string token, CancellationToken cancellationToken)
    {
        if (!_streams.TryGetValue(token, out var registration))
        {
            return null;
        }

        registration.LastAccessUtc = DateTime.UtcNow;
        using var request = new HttpRequestMessage(HttpMethod.Get, registration.Url);
        request.Headers.TryAddWithoutValidation("User-Agent", registration.UserAgent);
        using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
            .SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var manifestUrl = response.RequestMessage?.RequestUri ?? registration.Url;

        try
        {
            return DashManifestRewriter.Rewrite(body, manifestUrl);
        }
        catch (DvbIParseException ex)
        {
            _logger.LogWarning(ex, "Serving DASH manifest {Url} unchanged", registration.Url);
            return body;
        }
    }

    private void RemoveIdle()
    {
        var limit = DateTime.UtcNow - IdleTimeout;
        foreach (var token in _streams.Where(s => s.Value.LastAccessUtc < limit).Select(s => s.Key).ToList())
        {
            _streams.TryRemove(token, out _);
        }
    }

    private sealed record Registration(Uri Url, string UserAgent)
    {
        public DateTime LastAccessUtc { get; set; }
    }
}
