using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Tvheadend;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// A service instance Jellyfin can play: an IP instance (DASH/HLS) or a broadcast instance received
/// through TVHeadend.
/// </summary>
/// <param name="Instance">The DVB-I service instance.</param>
/// <param name="Tvheadend">The matching TVHeadend service for broadcast instances, null for IP instances.</param>
internal sealed record PlayableInstance(ServiceInstance Instance, TvhService? Tvheadend)
{
    /// <summary>
    /// Gets a value indicating whether the instance is played through TVHeadend.
    /// </summary>
    public bool IsBroadcast => Tvheadend is not null;
}
