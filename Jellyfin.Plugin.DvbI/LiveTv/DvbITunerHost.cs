using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Configuration;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using Jellyfin.Plugin.DvbI.Streaming;
using Jellyfin.Plugin.DvbI.Tvheadend;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.LiveTv;

/// <summary>
/// Jellyfin tuner type "dvbi". Every enabled service list of the plugin configuration is one tuner;
/// <see cref="TunerHostInfo.Url"/> holds the <see cref="ServiceListSource.Id"/>.
/// </summary>
public sealed class DvbITunerHost : ITunerHost, IConfigurableTunerHost
{
    /// <summary>
    /// Tuner type id.
    /// </summary>
    public const string TunerType = "dvbi";

    private const string ChannelIdPrefix = TunerType + "_";
    private const int DefaultFallbackBitrate = 30_000_000;
    private const int BroadcastAnalyzeDurationMs = 6000;
    private const string ProbeCacheVersion = "v2";

    /// <summary>
    /// User-Agent for media requests, the same browser-like default as Jellyfin's M3U tuner, because
    /// broadcaster CDNs target browsers and HbbTV devices rather than DVB-I metadata clients.
    /// </summary>
    private const string DefaultStreamUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36";

    private readonly IConfigurationManager _configurationManager;
    private readonly ServiceListProvider _serviceLists;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IStreamPreparer _streamPreparer;
    private readonly TvhClient _tvh;
    private readonly ILogger<DvbITunerHost> _logger;
    private readonly ConcurrentDictionary<string, List<TunerChannel>> _channels = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<(DvbIService Service, TvhMatch Match)>> _tvhMatches = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbITunerHost"/> class.
    /// </summary>
    /// <param name="configurationManager">Server configuration.</param>
    /// <param name="serviceLists">Service list provider.</param>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="dashProxy">DASH manifest proxy.</param>
    /// <param name="tvh">TVHeadend client.</param>
    /// <param name="logger">Logger.</param>
    public DvbITunerHost(
        IConfigurationManager configurationManager,
        ServiceListProvider serviceLists,
        IHttpClientFactory httpClientFactory,
        DashManifestProxy dashProxy,
        TvhClient tvh,
        ILogger<DvbITunerHost> logger)
    {
        _configurationManager = configurationManager;
        _serviceLists = serviceLists;
        _httpClientFactory = httpClientFactory;
        _streamPreparer = dashProxy;
        _tvh = tvh;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "DVB-I";

    /// <inheritdoc />
    public string Type => TunerType;

    /// <inheritdoc />
    public bool IsSupported => true;

    /// <summary>
    /// Gets the result of the last TVHeadend matching per service list, for the configuration page.
    /// </summary>
    internal IReadOnlyDictionary<string, IReadOnlyList<(DvbIService Service, TvhMatch Match)>> TvheadendMatches => _tvhMatches;

    private static PluginConfiguration PluginConfig => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    /// <summary>
    /// Drops the channel cache, e.g. after the plugin configuration changed.
    /// </summary>
    public void ClearCache() => _channels.Clear();

    /// <inheritdoc />
    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        var result = new List<ChannelInfo>();
        foreach (var tuner in GetTuners())
        {
            var channels = await GetTunerChannelsAsync(tuner, !enableCache, cancellationToken).ConfigureAwait(false);
            result.AddRange(channels.Select(c => c.Info));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var found = await FindChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            return [];
        }

        // One media source per delivery path ("DVB-T2 (TVHeadend)", "Internet (DASH)", ...): Jellyfin caches
        // stream probes per media source, so each path needs its own id, and users see and can pick the path.
        var (tuner, channel) = found.Value;
        return channel.Lineup.Instances.Select(p => CreateMediaSource(tuner, channel, PathOf(p), p)).ToList();
    }

    /// <inheritdoc />
    public async Task<ILiveStream> GetChannelStream(string channelId, string streamId, IList<ILiveStream> currentLiveStreams, CancellationToken cancellationToken)
    {
        var found = await FindChannelAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new LiveTvConflictException("Unknown DVB-I channel " + channelId);
        var (tuner, channel) = found;

        // The requested path first, then the others in DVB-I priority order (TS 103 770 clause 5.2.12:
        // fall back to lower priority instances on errors, e.g. no free tuner).
        var requested = channel.Lineup.Instances.FirstOrDefault(p => string.Equals(MediaSourceId(channel, p), streamId, StringComparison.OrdinalIgnoreCase));
        var candidates = requested is null
            ? channel.Lineup.Instances
            : channel.Lineup.Instances.Where(p => p != requested).Prepend(requested).ToList();

        foreach (var playable in candidates)
        {
            var stream = await OpenInstanceAsync(tuner, channel, playable, streamId, cancellationToken).ConfigureAwait(false);
            if (stream is null)
            {
                continue;
            }

            if (requested is not null && playable != requested)
            {
                // Jellyfin keys its probe cache by the requested media source. A different path delivers
                // different codecs (e.g. H.264 internet stream instead of HEVC broadcast), so skip the cached
                // probe instead of overwriting the requested path's entry.
                _logger.LogInformation("DVB-I {Service}: {Requested} not available, using {Actual}", channel.Lineup.Service.Name, SourceName(requested), SourceName(playable));
                stream.MediaSource.Id = streamId;
                stream.MediaSource.SupportsProbing = false;
            }

            return stream;
        }

        throw new LiveTvConflictException("No DVB-I service instance of " + channel.Lineup.Service.Name + " is reachable");
    }

    private async Task<DvbILiveStream?> OpenInstanceAsync(TunerHostInfo tuner, TunerChannel channel, PlayableInstance playable, string streamId, CancellationToken cancellationToken)
    {
        if (playable.IsBroadcast)
        {
            return await OpenBroadcastAsync(tuner, channel, playable, streamId, cancellationToken).ConfigureAwait(false);
        }

        var instance = playable.Instance;
        var url = await ResolveStreamUrlAsync(instance.Uri!, StreamUserAgent(tuner), cancellationToken).ConfigureAwait(false);
        if (url is null)
        {
            return null;
        }

        var prepared = _streamPreparer.Prepare(instance.Delivery, url, StreamUserAgent(tuner));
        _logger.LogInformation("Opening DVB-I {Service} via {Delivery} {Url}{Proxy}", channel.Lineup.Service.Name, instance.Delivery, url, prepared.IsLocal ? " (manifest proxy)" : string.Empty);
        var mediaSource = CreateMediaSource(tuner, channel, prepared.InputUrl, playable);
        mediaSource.IsRemote = !prepared.IsLocal;

        // ffmpeg's DASH demuxer never waits for the live edge: reading faster than real time it requests
        // segments that do not exist yet and skips them. Reading at native frame rate (-re) keeps it at the
        // distance set by the manifest preparation. HLS is paced by ffmpeg's HLS demuxer itself.
        mediaSource.ReadAtNativeFramerate = instance.Delivery == DeliveryKind.Dash;
        return new DvbILiveStream(mediaSource, tuner.Id, prepared.Release) { OriginalStreamId = streamId };
    }

    /// <summary>
    /// Gets all DVB-I channels with their service data, for the client API.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Channels of all DVB-I tuners.</returns>
    internal async Task<List<ChannelDetails>> GetChannelDetailsAsync(CancellationToken cancellationToken)
    {
        var result = new List<ChannelDetails>();
        foreach (var tuner in GetTuners())
        {
            var source = FindSource(tuner);
            var channels = await GetTunerChannelsAsync(tuner, false, cancellationToken).ConfigureAwait(false);
            result.AddRange(channels.Select(c => new ChannelDetails(c.Info, c.Lineup, c.List, source?.Name ?? string.Empty, source?.Url ?? string.Empty)));
        }

        return result;
    }

    /// <summary>
    /// Gets the DVB-I service behind a Jellyfin channel, for the programme guide.
    /// </summary>
    /// <param name="channelId">Jellyfin channel id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service list and service, or null if the channel is unknown.</returns>
    internal async Task<(ServiceList List, DvbIService Service)?> GetServiceAsync(string channelId, CancellationToken cancellationToken)
    {
        var found = await FindChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        return found is null ? null : (found.Value.Channel.List, found.Value.Channel.Lineup.Service);
    }

    /// <inheritdoc />
    public Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
        => Task.FromResult(new List<TunerHostInfo>());

    /// <inheritdoc />
    public Task Validate(TunerHostInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (FindSource(info) is null)
        {
            throw new ArgumentException("DVB-I tuners are managed on the DVB-I plugin settings page.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds a stable Jellyfin channel id for a service of a list.
    /// </summary>
    /// <param name="sourceId">Service list source id.</param>
    /// <param name="serviceId">Service UniqueIdentifier.</param>
    /// <returns>The channel id.</returns>
    internal static string GetChannelId(string sourceId, string serviceId)
        => ChannelIdPrefix + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sourceId + "|" + serviceId)))[..32];

    private static ServiceListSource? FindSource(TunerHostInfo tuner)
        => PluginConfig.ServiceLists.FirstOrDefault(s => s.Enabled && string.Equals(s.Id, tuner.Url, StringComparison.Ordinal));

    private IEnumerable<TunerHostInfo> GetTuners()
        => _configurationManager.GetConfiguration<LiveTvOptions>("livetv").TunerHosts
            .Where(t => string.Equals(t.Type, TunerType, StringComparison.OrdinalIgnoreCase));

    private async Task<List<TunerChannel>> GetTunerChannelsAsync(TunerHostInfo tuner, bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _channels.TryGetValue(tuner.Id, out var cached))
        {
            return cached;
        }

        var source = FindSource(tuner);
        if (source is null)
        {
            _logger.LogWarning("DVB-I tuner {Tuner} has no enabled service list in the plugin configuration", tuner.FriendlyName);
            return [];
        }

        try
        {
            var list = await _serviceLists.GetServiceListAsync(source.Url, forceRefresh, cancellationToken).ConfigureAwait(false);
            var broadcast = await BroadcastResolverAsync(source, list, forceRefresh, cancellationToken).ConfigureAwait(false);
            var lineup = ChannelLineup.Build(list, NullIfEmpty(source.RegionId), source.ChannelNumberOffset, PluginConfig.PreferHls, broadcast);
            var channels = lineup.Select(c => new TunerChannel(c, ToChannelInfo(tuner, source, c), list)).ToList();
            _channels[tuner.Id] = channels;
            _logger.LogInformation("DVB-I list {List} region {Region}: {Count} channels", source.Name, source.RegionName, channels.Count);
            return channels;
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "Loading DVB-I service list {Url} failed", source.Url);
            return _channels.TryGetValue(tuner.Id, out var previous) ? previous : [];
        }
    }

    private async Task<(TunerHostInfo Tuner, TunerChannel Channel)?> FindChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(channelId) || !channelId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        foreach (var tuner in GetTuners())
        {
            var channels = await GetTunerChannelsAsync(tuner, false, cancellationToken).ConfigureAwait(false);
            var channel = channels.FirstOrDefault(c => string.Equals(c.Info.Id, channelId, StringComparison.OrdinalIgnoreCase));
            if (channel is not null)
            {
                return (tuner, channel);
            }
        }

        return null;
    }

    private static ChannelInfo ToChannelInfo(TunerHostInfo tuner, ServiceListSource source, LineupChannel channel)
        => new()
        {
            Id = GetChannelId(source.Id, channel.Service.UniqueIdentifier),
            TunerChannelId = channel.Service.UniqueIdentifier,
            TunerHostId = tuner.Id,
            Name = channel.Service.Name,
            CallSign = channel.Service.Name,
            Number = channel.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ChannelType = channel.Service.Kind == DvbIServiceKind.Radio ? ChannelType.Radio : ChannelType.TV,
            ChannelGroup = channel.Service.ProviderName,
            ImageUrl = channel.Service.LogoUrl,
            HasImage = channel.Service.LogoUrl is not null,
            Path = PathOf(channel.Instances[0]).ToString(),
            Tags = []
        };

    /// <summary>
    /// Display name of the delivery path, shown by Jellyfin as media source name (e.g. in the playback
    /// info), so users can see whether a channel runs over broadcast or the internet.
    /// </summary>
    /// <param name="playable">Playable instance.</param>
    /// <returns>E.g. "DVB-T2 (TVHeadend)" or "Internet (DASH)".</returns>
    internal static string SourceName(PlayableInstance playable)
    {
        if (playable.Tvheadend is { } tvh)
        {
            var system = tvh.DeliverySystem ?? tvh.Delivery switch
            {
                DeliveryKind.DvbT => "DVB-T",
                DeliveryKind.DvbS => "DVB-S",
                DeliveryKind.DvbC => "DVB-C",
                _ => "Broadcast"
            };
            return system + " (TVHeadend)";
        }

        return playable.Instance.Delivery switch
        {
            DeliveryKind.Dash => "Internet (DASH)",
            DeliveryKind.Hls => "Internet (HLS)",
            _ => "Internet"
        };
    }

    private static MediaSourceInfo CreateMediaSource(TunerHostInfo tuner, TunerChannel channel, Uri url, PlayableInstance playable)
    {
        var isRadio = channel.Info.ChannelType == ChannelType.Radio;
        var streams = new List<MediaStream>();
        if (!isRadio)
        {
            streams.Add(new MediaStream { Type = MediaStreamType.Video, Index = -1, IsInterlaced = false });
        }

        streams.Add(new MediaStream { Type = MediaStreamType.Audio, Index = -1 });

        var mediaSource = new MediaSourceInfo
        {
            Id = MediaSourceId(channel, playable),
            Name = SourceName(playable),
            Path = url.ToString(),
            Protocol = MediaProtocol.Http,
            MediaStreams = streams,
            RequiresOpening = true,
            RequiresClosing = true,
            IsInfiniteStream = true,
            IsRemote = true,
            SupportsDirectPlay = false,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            RequiredHttpHeaders = new Dictionary<string, string> { ["User-Agent"] = StreamUserAgent(tuner) },
            UseMostCompatibleTranscodingProfile = !tuner.AllowFmp4TranscodingContainer,
            FallbackMaxStreamingBitrate = tuner.FallbackMaxStreamingBitrate > 0 ? tuner.FallbackMaxStreamingBitrate : DefaultFallbackBitrate
        };
        mediaSource.InferTotalBitrate();
        return mediaSource;
    }

    /// <summary>
    /// Requests the manifest and returns the URL after redirects. ffmpeg resolves relative segment URLs
    /// against the URL it was given, so it must get the final URL (e.g. ARD redirects to its CDN).
    /// Redirects are followed here because .NET refuses HTTPS to HTTP redirects. ARD redirects its HTTPS
    /// entry points to HTTP CDN URLs (for HbbTV devices); the CDN serves the same content over HTTPS, so
    /// a downgrade is first retried as HTTPS and only used as HTTP if that fails.
    /// </summary>
    private async Task<Uri?> ResolveStreamUrlAsync(Uri url, string userAgent, CancellationToken cancellationToken)
    {
        const int MaxRedirects = 10;
        var current = url;
        Uri? downgradeFallback = null;
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            int status;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                status = (int)response.StatusCode;
                if (status is >= 300 and < 400 && response.Headers.Location is not null)
                {
                    var target = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(response.RequestMessage?.RequestUri ?? current, response.Headers.Location);
                    downgradeFallback = null;
                    if (current.Scheme == Uri.UriSchemeHttps && target.Scheme == Uri.UriSchemeHttp)
                    {
                        downgradeFallback = target;
                        target = new UriBuilder(target) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
                    }

                    current = target;
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    return response.RequestMessage?.RequestUri ?? current;
                }
            }
            catch (HttpRequestException ex) when (downgradeFallback is not null)
            {
                _logger.LogDebug(ex, "HTTPS variant {Url} failed", current);
                status = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "DVB-I stream {Url} is not reachable", current);
                return null;
            }

            if (downgradeFallback is not null)
            {
                _logger.LogInformation("HTTPS variant of {Url} returned {Status}, using HTTP", downgradeFallback, status);
                current = downgradeFallback;
                downgradeFallback = null;
                continue;
            }

            _logger.LogWarning("DVB-I stream {Url} returned {Status}", current, status);
            return null;
        }

        _logger.LogWarning("DVB-I stream {Url} redirects too often", url);
        return null;
    }

    /// <summary>
    /// Media source id of one delivery path. Jellyfin caches the probe result of a live stream per media
    /// source id forever, so the id covers everything that changes the delivered stream (instance URL or
    /// TVHeadend service, DASH preparation, probe settings).
    /// </summary>
    private static string MediaSourceId(TunerChannel channel, PlayableInstance playable)
    {
        var rewrite = playable.Instance.Delivery == DeliveryKind.Dash && (Plugin.Instance?.Configuration.EnableDashRewrite ?? true);
        // Version 2: before per-path media sources a fallback could store the probe of another path under
        // this id (H.264 internet stream cached for the HEVC broadcast path); a new id forces a fresh probe.
        var key = ProbeCacheVersion + "|" + (playable.Tvheadend is { } tvh
            ? "tvheadend|" + tvh.Uuid + "|probe" + BroadcastAnalyzeDurationMs.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : playable.Instance.Uri + "|" + playable.Instance.Delivery + "|" + (rewrite ? "rewrite" : "plain"));
        return channel.Info.Id + "_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8];
    }

    /// <summary>
    /// Path of a delivery path before the stream is opened (informational; broadcast instances use the
    /// TVHeadend service stream address without credentials).
    /// </summary>
    private static Uri PathOf(PlayableInstance playable)
    {
        if (playable.Tvheadend is { } tvh && TvhSettings.From(PluginConfig) is { } settings)
        {
            return new Uri(settings.BaseUrl, "stream/service/" + tvh.Uuid);
        }

        return playable.Instance.Uri ?? new Uri("about:blank");
    }

    /// <summary>
    /// Builds the resolver that maps broadcast instances to TVHeadend services, or null if broadcast is off
    /// or TVHeadend cannot be reached (the channels then use their internet streams).
    /// </summary>
    private async Task<Func<DvbIService, ServiceInstance, TvhService?>?> BroadcastResolverAsync(ServiceListSource source, ServiceList list, bool forceRefresh, CancellationToken cancellationToken)
    {
        var config = PluginConfig;
        var settings = TvhSettings.From(config);
        if (!config.PreferBroadcast || settings is null)
        {
            _tvhMatches.TryRemove(source.Id, out _);
            return null;
        }

        List<TvhService> services;
        try
        {
            services = await _tvh.GetServicesAsync(settings, forceRefresh, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TVHeadend at {Url} is not reachable, using internet streams only", settings.BaseUrl);
            return null;
        }

        var matches = new List<(DvbIService Service, TvhMatch Match)>();
        foreach (var service in list.Services)
        {
            foreach (var instance in service.Instances)
            {
                if (TvhMatcher.Match(service, instance, services, config.TvheadendAllowEncrypted) is { } match)
                {
                    matches.Add((service, match));
                    if (match.Reason == TvhMatcher.NameMismatch)
                    {
                        _logger.LogWarning(
                            "DVB-I service {Service} {Delivery} {Triplet} is \"{TvhName}\" in TVHeadend; names differ, using internet streams",
                            service.Name,
                            instance.Delivery,
                            TvhMatcher.Triplet(instance),
                            match.Service?.Name);
                    }
                }
            }
        }

        _tvhMatches[source.Id] = matches;
        var usable = matches.Where(m => m.Match.Usable).ToDictionary(m => m.Match.Instance, m => m.Match.Service!);
        _logger.LogInformation("TVHeadend: {Usable} DVB-I broadcast instances usable, {Conflicts} name conflicts", usable.Count, matches.Count(m => m.Match.Reason == TvhMatcher.NameMismatch));
        return (_, instance) => usable.GetValueOrDefault(instance);
    }

    private async Task<DvbILiveStream?> OpenBroadcastAsync(TunerHostInfo tuner, TunerChannel channel, PlayableInstance playable, string streamId, CancellationToken cancellationToken)
    {
        var tvhService = playable.Tvheadend!;
        var settings = TvhSettings.From(PluginConfig);
        if (settings is null || tvhService.ChannelUuid is null)
        {
            return null;
        }

        Uri url;
        try
        {
            url = await _tvh.GetTicketUrlAsync(settings, tvhService.ChannelUuid, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TVHeadend did not issue a stream ticket for {Service}", tvhService.Name);
            return null;
        }

        // TVHeadend answers with an error when no tuner is free; then fall back to the next instance.
        if (!await _tvh.StreamAvailableAsync(settings, url, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        _logger.LogInformation("Opening DVB-I {Service} via TVHeadend {Delivery} \"{TvhService}\"", channel.Lineup.Service.Name, tvhService.Delivery, tvhService.Name);
        var mediaSource = CreateMediaSource(tuner, channel, url, playable);
        mediaSource.Container = "ts";
        mediaSource.IsRemote = false;
        mediaSource.ReadAtNativeFramerate = false;

        // DVB-T2 uses HEVC; a stream joined between key frames needs more than Jellyfin's default 3 s to probe.
        mediaSource.AnalyzeDurationMs = BroadcastAnalyzeDurationMs;
        return new DvbILiveStream(mediaSource, tuner.Id, static () => { }) { OriginalStreamId = streamId };
    }

    private static string StreamUserAgent(TunerHostInfo tuner)
        => string.IsNullOrWhiteSpace(tuner.UserAgent) ? DefaultStreamUserAgent : tuner.UserAgent;

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private sealed record TunerChannel(LineupChannel Lineup, ChannelInfo Info, ServiceList List);
}
