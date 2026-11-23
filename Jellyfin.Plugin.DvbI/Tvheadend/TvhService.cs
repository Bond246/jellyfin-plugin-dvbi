using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// A TVHeadend service (mpegts/service) with the DVB identifiers of its multiplex.
/// </summary>
/// <param name="Uuid">Service UUID.</param>
/// <param name="Name">Service name as broadcast (SDT).</param>
/// <param name="ServiceId">DVB service_id.</param>
/// <param name="OriginalNetworkId">original_network_id of the multiplex, null if unknown.</param>
/// <param name="TransportStreamId">transport_stream_id of the multiplex, null if unknown.</param>
/// <param name="Delivery">DvbT, DvbS or DvbC.</param>
/// <param name="Encrypted">Whether the service is scrambled.</param>
/// <param name="ChannelUuid">UUID of the TVHeadend channel the service is mapped to, if any (needed for stream tickets).</param>
internal sealed record TvhService(
    string Uuid,
    string Name,
    int ServiceId,
    int? OriginalNetworkId,
    int? TransportStreamId,
    DeliveryKind Delivery,
    bool Encrypted,
    string? ChannelUuid)
{
    /// <summary>
    /// Gets the delivery system as reported by TVHeadend, e.g. "DVB-T2" or "DVB-S2".
    /// </summary>
    public string? DeliverySystem { get; init; }
}
