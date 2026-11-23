using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.DvbI.Listings;
using Jellyfin.Plugin.DvbI.LiveTv;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// Client API with the original DVB-I data: stream URLs of all service instances, broadcast parameters,
/// linked (HbbTV) applications, the DVB-I schedule with on-demand offers and More Episodes.
/// Intended for dedicated TV clients and HbbTV integrations that play or present DVB-I themselves.
/// Available to every signed-in user.
/// </summary>
[ApiController]
[Route("DvbI/Channels")]
[Authorize]
[Produces(MediaTypeNames.Application.Json)]
public class DvbIClientController : ControllerBase
{
    private static readonly TimeSpan DefaultScheduleRange = TimeSpan.FromHours(12);
    private static readonly TimeSpan MaxScheduleRange = TimeSpan.FromDays(7);

    private readonly DvbITunerHost _tunerHost;
    private readonly DvbIListingsProvider _listings;
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIClientController"/> class.
    /// </summary>
    /// <param name="tunerHost">DVB-I tuner host.</param>
    /// <param name="listings">DVB-I guide provider.</param>
    /// <param name="libraryManager">Library manager, to resolve Jellyfin channel ids.</param>
    public DvbIClientController(DvbITunerHost tunerHost, DvbIListingsProvider listings, ILibraryManager libraryManager)
    {
        _tunerHost = tunerHost;
        _listings = listings;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Gets all DVB-I channels.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Channels ordered by number.</returns>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ChannelDto>>> GetChannels(CancellationToken cancellationToken)
    {
        var channels = await _tunerHost.GetChannelDetailsAsync(cancellationToken).ConfigureAwait(false);
        var ids = JellyfinChannelIds();
        return Ok(channels
            .OrderBy(c => c.Lineup.Number)
            .Select(c => ClientDtoMapper.ToDto(c, ids.TryGetValue(c.Info.Id, out var id) ? id : null))
            .ToList());
    }

    /// <summary>
    /// Gets one DVB-I channel.
    /// </summary>
    /// <param name="channelId">Jellyfin channel item id or DVB-I channel id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The channel.</returns>
    [HttpGet("{channelId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ChannelDto>> GetChannel([FromRoute, Required] string channelId, CancellationToken cancellationToken)
    {
        var (channel, jellyfinId) = await FindAsync(channelId, cancellationToken).ConfigureAwait(false);
        return channel is null ? NotFound() : Ok(ClientDtoMapper.ToDto(channel, jellyfinId));
    }

    /// <summary>
    /// Gets the DVB-I schedule of a channel, including on-demand offers (e.g. HbbTV media library deep links).
    /// </summary>
    /// <param name="channelId">Jellyfin channel item id or DVB-I channel id.</param>
    /// <param name="start">Range start (UTC), default now.</param>
    /// <param name="end">Range end (UTC), default start + 12 h, at most 7 days after start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Scheduled programmes.</returns>
    [HttpGet("{channelId}/Schedule")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ScheduleEventDto>>> GetSchedule(
        [FromRoute, Required] string channelId,
        [FromQuery] DateTime? start,
        [FromQuery] DateTime? end,
        CancellationToken cancellationToken)
    {
        var from = (start ?? DateTime.UtcNow).ToUniversalTime();
        var to = (end ?? from + DefaultScheduleRange).ToUniversalTime();
        if (to <= from || to - from > MaxScheduleRange)
        {
            return BadRequest("end must be after start and at most 7 days later");
        }

        var (channel, _) = await FindAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            return NotFound();
        }

        var events = await _listings.GetEventsAsync(channel.List, channel.Lineup.Service, from, to, cancellationToken).ConfigureAwait(false);
        return Ok(events.Select(ClientDtoMapper.ToDto).ToList());
    }

    /// <summary>
    /// Gets further episodes of a programme from the content guide server (TS 103 770 clause 6.7).
    /// </summary>
    /// <param name="channelId">Jellyfin channel item id or DVB-I channel id.</param>
    /// <param name="crid">Programme CRID (from the schedule).</param>
    /// <param name="page">NextPage link of a previous response, unmodified.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One page of programmes.</returns>
    [HttpGet("{channelId}/MoreEpisodes")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<MoreEpisodesDto>> GetMoreEpisodes(
        [FromRoute, Required] string channelId,
        [FromQuery, Required] string crid,
        [FromQuery] string? page,
        CancellationToken cancellationToken)
    {
        var (channel, _) = await FindAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            return NotFound();
        }

        try
        {
            var result = await _listings.GetMoreEpisodesAsync(channel.List, channel.Lineup.Service, crid, page, cancellationToken).ConfigureAwait(false);
            return result is null
                ? NotFound("The channel has no More Episodes endpoint or the page link does not belong to it.")
                : Ok(new MoreEpisodesDto(result.Value.Programmes.Select(ClientDtoMapper.ToDto).ToList(), result.Value.NextPage));
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private async Task<(ChannelDetails? Channel, Guid? JellyfinId)> FindAsync(string channelId, CancellationToken cancellationToken)
    {
        var channels = await _tunerHost.GetChannelDetailsAsync(cancellationToken).ConfigureAwait(false);
        var ids = JellyfinChannelIds();
        if (Guid.TryParse(channelId, out var guid))
        {
            var externalId = ids.FirstOrDefault(kv => kv.Value == guid).Key;
            var byJellyfinId = externalId is null ? null : channels.FirstOrDefault(c => string.Equals(c.Info.Id, externalId, StringComparison.OrdinalIgnoreCase));
            return (byJellyfinId, byJellyfinId is null ? null : guid);
        }

        var channel = channels.FirstOrDefault(c => string.Equals(c.Info.Id, channelId, StringComparison.OrdinalIgnoreCase));
        return (channel, channel is not null && ids.TryGetValue(channel.Info.Id, out var id) ? id : null);
    }

    /// <summary>
    /// Maps DVB-I channel ids (Jellyfin external ids) to Jellyfin item ids.
    /// </summary>
    private Dictionary<string, Guid> JellyfinChannelIds()
        => _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.LiveTvChannel] })
            .Where(i => i.ExternalId?.StartsWith(DvbITunerHost.TunerType + "_", StringComparison.OrdinalIgnoreCase) ?? false)
            .GroupBy(i => i.ExternalId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
}
