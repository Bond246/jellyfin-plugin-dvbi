using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Http;
using Jellyfin.Plugin.DvbI.LiveTv;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.Listings;

/// <summary>
/// Jellyfin guide provider "dvbi": programme guide from the content guide servers referenced in the
/// DVB-I service lists (TS 103 770 clause 6.5, TV-Anytime). Its channels have the same ids as the
/// DVB-I tuner channels, so Jellyfin maps them without user interaction.
/// </summary>
public sealed class DvbIListingsProvider : IListingsProvider, IDisposable
{
    /// <summary>
    /// Guide provider type id.
    /// </summary>
    public const string ProviderType = "dvbi";

    /// <summary>
    /// Schedule requests use 12 hour windows starting at multiples of 3 hours (clause 6.5.2.1).
    /// </summary>
    internal const long WindowSeconds = 43_200;

    private const long AlignmentSeconds = 10_800;
    private const int MaxParallelRequests = 4;

    private readonly DvbITunerHost _tunerHost;
    private readonly DvbIHttpClient _http;
    private readonly IServerConfigurationManager _serverConfig;
    private readonly ILogger<DvbIListingsProvider> _logger;
    private readonly SemaphoreSlim _requestLimit = new(MaxParallelRequests, MaxParallelRequests);
    private static readonly TimeSpan FailedWindowRetryDelay = TimeSpan.FromHours(6);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<string, DateTime> _failedWindows = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIListingsProvider"/> class.
    /// </summary>
    /// <param name="tunerHost">DVB-I tuner host.</param>
    /// <param name="http">DVB-I HTTP client.</param>
    /// <param name="serverConfig">Server configuration (metadata language).</param>
    /// <param name="logger">Logger.</param>
    public DvbIListingsProvider(DvbITunerHost tunerHost, DvbIHttpClient http, IServerConfigurationManager serverConfig, ILogger<DvbIListingsProvider> logger)
    {
        _tunerHost = tunerHost;
        _http = http;
        _serverConfig = serverConfig;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DVB-I";

    /// <inheritdoc />
    public string Type => ProviderType;

    private bool German => (_serverConfig.Configuration.PreferredMetadataLanguage ?? _serverConfig.Configuration.UICulture ?? string.Empty)
        .StartsWith("de", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(ListingsProviderInfo info, string channelId, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        var found = await _tunerHost.GetServiceAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return [];
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var (list, service) = found.Value;
        var events = await GetEventsAsync(list, service, startDateUtc, endDateUtc, cancellationToken).ConfigureAwait(false);
        var german = German;
        var programs = events.Select(e => ProgramMapper.ToProgramInfo(e, channelId, german)).ToList();
        _logger.LogDebug("DVB-I guide for {Service}: {Count} programmes in {Elapsed} ms", service.Name, programs.Count, stopwatch.ElapsedMilliseconds);
        return programs;
    }

    /// <summary>
    /// Gets the schedule of a service from its content guide server.
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <param name="service">Service.</param>
    /// <param name="startDateUtc">Range start.</param>
    /// <param name="endDateUtc">Range end.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Events overlapping the range, ordered by start time.</returns>
    internal async Task<List<EpgEvent>> GetEventsAsync(ServiceList list, DvbIService service, DateTime startDateUtc, DateTime endDateUtc, CancellationToken cancellationToken)
    {
        var guide = list.GetGuideSource(service);
        if (guide is null)
        {
            _logger.LogDebug("DVB-I service {Service} has no content guide source", service.Name);
            return [];
        }

        var windows = GetWindows(startDateUtc, endDateUtc, DateTime.UtcNow);
        var tasks = windows.Select(w => FetchWindowAsync(guide.Value.Endpoint, guide.Value.ServiceId, w.Start, w.End, cancellationToken));
        var events = (await Task.WhenAll(tasks).ConfigureAwait(false)).SelectMany(e => e);
        return events
            .Where(e => e.EndUtc > startDateUtc && e.StartUtc < endDateUtc)
            .DistinctBy(e => (e.Crid, e.StartUtc))
            .OrderBy(e => e.StartUtc)
            .ToList();
    }

    /// <summary>
    /// Requests further episodes of a programme (TS 103 770 clause 6.7) or a following result page.
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <param name="service">Service.</param>
    /// <param name="crid">Programme CRID, used for the first page.</param>
    /// <param name="page">A "next" link from a previous response, used unmodified (clause 6.9).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Programmes and the next page link, or null if the service has no More Episodes endpoint or the page link is foreign.</returns>
    /// <exception cref="HttpRequestException">The content guide server could not be reached.</exception>
    internal async Task<(List<ProgrammeSummary> Programmes, string? NextPage)?> GetMoreEpisodesAsync(ServiceList list, DvbIService service, string crid, string? page, CancellationToken cancellationToken)
    {
        var url = MoreEpisodesUrl(list.GetContentGuideSource(service)?.MoreEpisodesEndpoint, crid, page);
        if (url is null)
        {
            return null;
        }

        var body = await _http.GetStringAsync(url, false, cancellationToken).ConfigureAwait(false);
        return ScheduleParser.ParseProgrammes(body);
    }

    /// <inheritdoc />
    public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        => Task.FromResult(new List<NameIdPair>());

    /// <inheritdoc />
    public async Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
    {
        var channels = await _tunerHost.GetChannels(true, cancellationToken).ConfigureAwait(false);
        return channels.Select(c => new ChannelInfo
        {
            Id = c.Id,
            Name = c.Name,
            Number = c.Number,
            CallSign = c.CallSign,
            ImageUrl = c.ImageUrl,
            HasImage = c.HasImage,
            ChannelType = c.ChannelType,
        }).ToList();
    }

    /// <inheritdoc />
    public void Dispose() => _requestLimit.Dispose();

    /// <summary>
    /// Splits a time range into the request windows allowed by TS 103 770 clause 6.5.2.1:
    /// 12 hours, starting at a multiple of 3 hours, within 28 days before today and 28 days after today.
    /// </summary>
    /// <param name="startUtc">Range start.</param>
    /// <param name="endUtc">Range end.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <returns>Unix time windows.</returns>
    internal static List<(long Start, long End)> GetWindows(DateTime startUtc, DateTime endUtc, DateTime nowUtc)
    {
        var today = new DateTimeOffset(nowUtc.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        var earliest = today - (28 * 86_400);
        var latest = today + (29 * 86_400);

        var start = Math.Max(new DateTimeOffset(startUtc, TimeSpan.Zero).ToUnixTimeSeconds(), earliest);
        var end = Math.Min(new DateTimeOffset(endUtc, TimeSpan.Zero).ToUnixTimeSeconds(), latest);
        var windows = new List<(long Start, long End)>();
        for (var s = start - (((start % AlignmentSeconds) + AlignmentSeconds) % AlignmentSeconds); s < end && s + WindowSeconds <= latest; s += WindowSeconds)
        {
            windows.Add((s, s + WindowSeconds));
        }

        return windows;
    }

    /// <summary>
    /// Builds a schedule request URL (clause 6.5.2.2), parameters in the specified order.
    /// </summary>
    /// <param name="endpoint">ScheduleInfoEndpoint.</param>
    /// <param name="serviceId">Service id.</param>
    /// <param name="start">Window start (unix time).</param>
    /// <param name="end">Window end (unix time).</param>
    /// <returns>Request URL.</returns>
    internal static string ScheduleUrl(string endpoint, string serviceId, long start, long end)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}{1}start={2}&end={3}&sid={4}",
            endpoint,
            endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?",
            start,
            end,
            Uri.EscapeDataString(serviceId));

    /// <summary>
    /// Builds a More Episodes request URL (clause 6.7) or validates a pagination link (clause 6.9).
    /// Pagination links are used unmodified but only if they point to the endpoint's server, so the
    /// client API cannot be used to fetch arbitrary URLs.
    /// </summary>
    /// <param name="endpoint">MoreEpisodesEndpoint.</param>
    /// <param name="crid">Programme CRID.</param>
    /// <param name="page">Pagination link or null.</param>
    /// <returns>The URL, or null if there is no endpoint or the link is foreign.</returns>
    internal static string? MoreEpisodesUrl(string? endpoint, string crid, string? page)
    {
        if (endpoint is null || !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            return null;
        }

        if (string.IsNullOrEmpty(page))
        {
            return endpoint + (endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "pid=" + Uri.EscapeDataString(crid) + "&type=ondemand";
        }

        return Uri.TryCreate(page, UriKind.Absolute, out var pageUri)
            && string.Equals(pageUri.GetLeftPart(UriPartial.Authority), endpointUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
            ? page
            : null;
    }

    private async Task<List<EpgEvent>> FetchWindowAsync(string endpoint, string serviceId, long start, long end, CancellationToken cancellationToken)
    {
        var url = ScheduleUrl(endpoint, serviceId, start, end);
        if (_failedWindows.TryGetValue(url, out var failedAt) && DateTime.UtcNow - failedAt < FailedWindowRetryDelay)
        {
            return [];
        }

        await _requestLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string body;
            try
            {
                body = await _http.GetStringAsync(url, false, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is null || (int)ex.StatusCode >= 500)
            {
                // Content guide servers occasionally answer bursts with 502/503; retry once after a pause.
                _logger.LogDebug("DVB-I schedule request {Url} failed ({Status}), retrying", url, ex.StatusCode);
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
                body = await _http.GetStringAsync(url, false, cancellationToken).ConfigureAwait(false);
            }

            return ScheduleParser.Parse(body);
        }
        catch (HttpRequestException ex)
        {
            // Windows beyond the provider's horizon are often answered with 4xx. Do not repeat the same
            // request right away (clause 6.5.2.1); the horizon moves, so try again after a while.
            if (ex.StatusCode is { } status && (int)status is >= 400 and < 500)
            {
                _failedWindows[url] = DateTime.UtcNow;
            }

            _logger.LogDebug(ex, "DVB-I schedule request {Url} failed", url);
            return [];
        }
        catch (DvbIParseException ex)
        {
            _logger.LogWarning(ex, "DVB-I schedule response {Url} is invalid", url);
            return [];
        }
        finally
        {
            _requestLimit.Release();
        }
    }
}
