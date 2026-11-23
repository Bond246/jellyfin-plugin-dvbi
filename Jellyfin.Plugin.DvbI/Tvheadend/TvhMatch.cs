using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// Result of matching a DVB-I broadcast instance against TVHeadend.
/// </summary>
/// <param name="Instance">The DVB-I service instance.</param>
/// <param name="Service">The TVHeadend service with the same triplet, if any.</param>
/// <param name="Usable">Whether the instance can be played through TVHeadend.</param>
/// <param name="Reason">Why it cannot be used (NotFound, NameMismatch, Encrypted, NoChannel), null if usable.</param>
internal sealed record TvhMatch(ServiceInstance Instance, TvhService? Service, bool Usable, string? Reason);
