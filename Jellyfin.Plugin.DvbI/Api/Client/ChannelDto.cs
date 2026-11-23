using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// A DVB-I channel with the original service data, for clients that want to play or present it themselves.
/// </summary>
/// <param name="JellyfinId">Jellyfin item id of the Live TV channel (null until the guide has been refreshed).</param>
/// <param name="ChannelId">DVB-I channel id (Jellyfin external channel id).</param>
/// <param name="Number">Channel number in Jellyfin.</param>
/// <param name="Name">Service name.</param>
/// <param name="Provider">Provider name.</param>
/// <param name="Kind">Tv or Radio.</param>
/// <param name="LogoUrl">Service logo.</param>
/// <param name="ServiceId">Service UniqueIdentifier.</param>
/// <param name="ServiceListName">Configured service list name.</param>
/// <param name="ServiceListUrl">Configured service list URL.</param>
/// <param name="Instances">All service instances in DVB-I priority order.</param>
/// <param name="LinkedApplications">Applications linked to the service (e.g. HbbTV).</param>
/// <param name="Guide">Programme guide source, if any.</param>
public sealed record ChannelDto(
    Guid? JellyfinId,
    string ChannelId,
    string? Number,
    string Name,
    string Provider,
    string Kind,
    string? LogoUrl,
    string ServiceId,
    string ServiceListName,
    string ServiceListUrl,
    IReadOnlyList<ServiceInstanceDto> Instances,
    IReadOnlyList<LinkedApplicationDto> LinkedApplications,
    GuideSourceDto? Guide);
