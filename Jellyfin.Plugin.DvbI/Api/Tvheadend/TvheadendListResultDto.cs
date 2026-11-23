using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Tvheadend;

/// <summary>
/// Matching result for one configured service list.
/// </summary>
/// <param name="ServiceList">Service list name.</param>
/// <param name="Usable">Services that can be played through TVHeadend.</param>
/// <param name="BroadcastOnly">Of those, services without internet stream that become channels through TVHeadend.</param>
/// <param name="NotReceived">Services with broadcast instances that TVHeadend does not receive.</param>
/// <param name="UsableServices">Names of the usable services.</param>
/// <param name="Conflicts">Instances that cannot be used, with reason.</param>
public sealed record TvheadendListResultDto(
    string ServiceList,
    int Usable,
    int BroadcastOnly,
    int NotReceived,
    IReadOnlyList<string> UsableServices,
    IReadOnlyList<TvheadendConflictDto> Conflicts);
