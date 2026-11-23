using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api;

/// <summary>
/// Endpoints used by the plugin configuration page.
/// </summary>
[ApiController]
[Route("DvbI")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class DvbIController : ControllerBase
{
    private readonly ServiceListProvider _serviceLists;

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIController"/> class.
    /// </summary>
    /// <param name="serviceLists">Service list provider.</param>
    public DvbIController(ServiceListProvider serviceLists)
    {
        _serviceLists = serviceLists;
    }

    /// <summary>
    /// Queries a service list registry.
    /// </summary>
    /// <param name="url">Registry query URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The offered service lists.</returns>
    [HttpGet("Registry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IEnumerable<OfferingDto>>> GetRegistry([FromQuery, Required] Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            var offerings = await _serviceLists.GetRegistryAsync(url.ToString(), cancellationToken).ConfigureAwait(false);
            return Ok(offerings.Select(o => new OfferingDto(o.Name, o.Url, o.ProviderName, o.Deliveries, o.Countries)));
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Loads a service list and returns its regions.
    /// </summary>
    /// <param name="url">Service list URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary and region tree.</returns>
    [HttpGet("ServiceList")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ServiceListDto>> GetServiceList([FromQuery, Required] Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            var list = await _serviceLists.GetServiceListAsync(url.ToString(), false, cancellationToken).ConfigureAwait(false);
            var regions = ChannelLineup.FlattenRegions(list)
                .Select(n => new RegionDto(n.Region.Id, n.Region.Name, n.Depth, n.Region.Selectable))
                .ToList();
            var ipServices = list.Services.Count(s => s.Instances.Any(i => i.IsIp && !i.HasContentProtection));
            return Ok(new ServiceListDto(list.Name, list.ProviderName, list.Version, list.Services.Count, ipServices, regions));
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Finds the region of a postcode.
    /// </summary>
    /// <param name="url">Service list URL.</param>
    /// <param name="postcode">Postcode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The most specific matching region.</returns>
    [HttpGet("ServiceList/Region")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<RegionDto>> GetRegionForPostcode([FromQuery, Required] Uri url, [FromQuery, Required] string postcode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            var list = await _serviceLists.GetServiceListAsync(url.ToString(), false, cancellationToken).ConfigureAwait(false);
            var node = ChannelLineup.FindRegionByPostcode(list, postcode);
            return node is null
                ? NotFound()
                : Ok(new RegionDto(node.Region.Id, node.Region.Name, node.Depth, node.Region.Selectable));
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>
    /// Previews the channels Jellyfin will get for a list and region.
    /// </summary>
    /// <param name="url">Service list URL.</param>
    /// <param name="regionId">Region id, empty for none.</param>
    /// <param name="offset">Channel number offset.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Channels.</returns>
    [HttpGet("ServiceList/Channels")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IEnumerable<ChannelDto>>> GetChannels([FromQuery, Required] Uri url, [FromQuery] string? regionId, [FromQuery] int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        try
        {
            var list = await _serviceLists.GetServiceListAsync(url.ToString(), false, cancellationToken).ConfigureAwait(false);
            var preferHls = Plugin.Instance?.Configuration.PreferHls ?? false;
            var lineup = ChannelLineup.Build(list, string.IsNullOrEmpty(regionId) ? null : regionId, offset, preferHls);
            return Ok(lineup.Select(c => new ChannelDto(c.Number, c.Service.Name, c.Service.Kind.ToString(), c.Instances.Select(i => i.Instance.Delivery + (i.IsBroadcast ? " (TVHeadend)" : string.Empty)).ToList())));
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }
}
