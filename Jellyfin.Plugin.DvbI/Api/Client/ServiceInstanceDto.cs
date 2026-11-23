using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// A service instance as signalled in the DVB-I service list (TS 103 770 clause 5.5.3).
/// </summary>
/// <param name="Priority">Priority, lower value = higher priority.</param>
/// <param name="Delivery">Dash, Hls, DvbT, DvbS, DvbC or Other.</param>
/// <param name="Url">Original stream URL for IP deliveries (DASH MPD, HLS playlist).</param>
/// <param name="DisplayName">Instance display name.</param>
/// <param name="ContentProtection">Whether content protection (DRM/CA) is signalled.</param>
/// <param name="PlayableInJellyfin">Whether Jellyfin uses this instance.</param>
/// <param name="BroadcastParameters">Delivery parameters of broadcast instances (element name, or element@attribute, to value).</param>
/// <param name="LinkedApplications">Applications linked to this instance.</param>
/// <param name="TvheadendService">Name of the TVHeadend service used for this broadcast instance, if any.</param>
public sealed record ServiceInstanceDto(
    int Priority,
    string Delivery,
    string? Url,
    string? DisplayName,
    bool ContentProtection,
    bool PlayableInJellyfin,
    IReadOnlyDictionary<string, string> BroadcastParameters,
    IReadOnlyList<LinkedApplicationDto> LinkedApplications,
    string? TvheadendService);
