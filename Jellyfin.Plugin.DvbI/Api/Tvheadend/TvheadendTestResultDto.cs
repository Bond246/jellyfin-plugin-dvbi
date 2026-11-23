using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Tvheadend;

/// <summary>
/// Result of a TVHeadend connection test.
/// </summary>
/// <param name="Version">TVHeadend version.</param>
/// <param name="ApiVersion">TVHeadend API version.</param>
/// <param name="Services">Number of enabled DVB services in TVHeadend.</param>
/// <param name="Lists">Matching result per enabled service list.</param>
public sealed record TvheadendTestResultDto(string Version, int ApiVersion, int Services, IReadOnlyList<TvheadendListResultDto> Lists);
