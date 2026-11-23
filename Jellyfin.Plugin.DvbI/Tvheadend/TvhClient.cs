using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// Minimal TVHeadend HTTP API client (tested with TVHeadend 4.3, API version 20).
/// Streams are opened with per-channel tickets (<c>/playlist/ticket/channel/{uuid}</c>), so the stream URL
/// handed to ffmpeg contains no credentials (Jellyfin logs ffmpeg command lines).
/// </summary>
public sealed class TvhClient : IDisposable
{
    private static readonly TimeSpan ServiceCacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly ILogger<TvhClient> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private (string Key, HttpClient Client)? _client;
    private (string Key, DateTime LoadedUtc, List<TvhService> Services)? _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvhClient"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public TvhClient(ILogger<TvhClient> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the TVHeadend services, cached for a few minutes.
    /// </summary>
    /// <param name="settings">Connection settings.</param>
    /// <param name="forceRefresh">Ignore the cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enabled DVB services.</returns>
    /// <exception cref="HttpRequestException">TVHeadend is not reachable or rejects the credentials.</exception>
    internal async Task<List<TvhService>> GetServicesAsync(TvhSettings settings, bool forceRefresh, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _services is { } cached && cached.Key == settings.Key && DateTime.UtcNow - cached.LoadedUtc < ServiceCacheDuration)
            {
                return cached.Services;
            }

            var client = Client(settings);
            using var muxes = await GetJsonAsync(client, settings, "api/mpegts/mux/grid?limit=100000", cancellationToken).ConfigureAwait(false);
            using var services = await GetJsonAsync(client, settings, "api/mpegts/service/grid?limit=100000", cancellationToken).ConfigureAwait(false);

            var muxInfo = new Dictionary<string, (int? Onid, int? Tsid, DeliveryKind Delivery, string? Delsys)>(StringComparer.Ordinal);
            foreach (var mux in Entries(muxes))
            {
                muxInfo[Str(mux, "uuid") ?? string.Empty] = (Id(mux, "onid"), Id(mux, "tsid"), Delivery(Str(mux, "delsys")), Str(mux, "delsys"));
            }

            var result = new List<TvhService>();
            foreach (var service in Entries(services))
            {
                if (!Bool(service, "enabled") || Int(service, "sid") is not { } sid
                    || !muxInfo.TryGetValue(Str(service, "multiplex_uuid") ?? string.Empty, out var mux)
                    || mux.Delivery == DeliveryKind.Other)
                {
                    continue;
                }

                var channel = service.TryGetProperty("channel", out var channels) && channels.ValueKind == JsonValueKind.Array && channels.GetArrayLength() > 0
                    ? channels[0].GetString()
                    : null;
                result.Add(new TvhService(Str(service, "uuid") ?? string.Empty, Str(service, "svcname") ?? string.Empty, sid, mux.Onid, mux.Tsid, mux.Delivery, Bool(service, "encrypted"), channel)
                {
                    DeliverySystem = mux.Delsys,
                });
            }

            _services = (settings.Key, DateTime.UtcNow, result);
            _logger.LogDebug("TVHeadend: {Count} services loaded", result.Count);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets a ticket stream URL for a TVHeadend channel.
    /// </summary>
    /// <param name="settings">Connection settings.</param>
    /// <param name="channelUuid">Channel UUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stream URL with ticket and profile, without credentials.</returns>
    /// <exception cref="HttpRequestException">TVHeadend did not issue a ticket.</exception>
    internal async Task<Uri> GetTicketUrlAsync(TvhSettings settings, string channelUuid, CancellationToken cancellationToken)
    {
        using var response = await Client(settings).GetAsync(new Uri(settings.BaseUrl, "playlist/ticket/channel/" + Uri.EscapeDataString(channelUuid)), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var playlist = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var line = playlist.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            ?? throw new HttpRequestException("TVHeadend returned no stream URL for channel " + channelUuid);
        return WithProfile(new Uri(line), settings.Profile);
    }

    /// <summary>
    /// Checks whether a stream starts (TVHeadend answers with an error if no tuner is free), then closes it.
    /// </summary>
    /// <param name="settings">Connection settings.</param>
    /// <param name="url">Ticket stream URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if TVHeadend delivers the stream.</returns>
    internal async Task<bool> StreamAvailableAsync(TvhSettings settings, Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client(settings).GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TVHeadend stream returned {Status}", (int)response.StatusCode);
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TVHeadend stream did not start");
            return false;
        }
    }

    /// <summary>
    /// Reads the TVHeadend server information (connection test).
    /// </summary>
    /// <param name="settings">Connection settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Software version and API version.</returns>
    /// <exception cref="HttpRequestException">Not reachable or not authorised.</exception>
    internal async Task<(string Version, int ApiVersion)> GetServerInfoAsync(TvhSettings settings, CancellationToken cancellationToken)
    {
        using var info = await GetJsonAsync(Client(settings), settings, "api/serverinfo", cancellationToken).ConfigureAwait(false);
        return (Str(info.RootElement, "sw_version") ?? "?", Int(info.RootElement, "api_version") ?? 0);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _client?.Client.Dispose();
        _lock.Dispose();
    }

    private static Uri WithProfile(Uri url, string profile)
    {
        var query = url.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("profile=", StringComparison.OrdinalIgnoreCase))
            .Append("profile=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(profile) ? "pass" : profile));
        return new UriBuilder(url) { Query = string.Join('&', query) }.Uri;
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, TvhSettings settings, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(settings.BaseUrl, path), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }

    private static IEnumerable<JsonElement> Entries(JsonDocument document)
        => document.RootElement.TryGetProperty("entries", out var entries) && entries.ValueKind == JsonValueKind.Array
            ? entries.EnumerateArray()
            : [];

    private static string? Str(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : null;

    private static bool Bool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>TVHeadend reports 65536 for identifiers it has not seen (e.g. mux not scanned).</summary>
    private static int? Id(JsonElement element, string name)
        => Int(element, name) is { } value && value is >= 0 and <= 65535 ? value : null;

    private static DeliveryKind Delivery(string? delsys)
        => delsys?.ToUpperInvariant() switch
        {
            { } d when d.StartsWith("DVB-T", StringComparison.Ordinal) || d.StartsWith("DVBT", StringComparison.Ordinal) => DeliveryKind.DvbT,
            { } d when d.StartsWith("DVB-S", StringComparison.Ordinal) || d.StartsWith("DVBS", StringComparison.Ordinal) => DeliveryKind.DvbS,
            { } d when d.StartsWith("DVB-C", StringComparison.Ordinal) || d.StartsWith("DVBC", StringComparison.Ordinal) => DeliveryKind.DvbC,
            _ => DeliveryKind.Other
        };

    private HttpClient Client(TvhSettings settings)
    {
        if (_client is { } existing && existing.Key == settings.Key)
        {
            return existing.Client;
        }

        // TVHeadend uses Digest authentication by default; .NET's own Digest implementation is rejected by it.
        var handler = new DigestAuthHandler(settings.Username, settings.Password, new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) });
        var client = new HttpClient(handler) { Timeout = RequestTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Jellyfin-DVB-I-Plugin/" + (typeof(TvhClient).Assembly.GetName().Version?.ToString(3) ?? "0").ToString(CultureInfo.InvariantCulture));
        var previous = _client;
        _client = (settings.Key, client);
        previous?.Client.Dispose();
        return client;
    }
}
