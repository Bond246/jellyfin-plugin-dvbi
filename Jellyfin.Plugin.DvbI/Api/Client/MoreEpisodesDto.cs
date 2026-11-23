using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// One page of a More Episodes result.
/// </summary>
/// <param name="Programmes">Programmes on this page.</param>
/// <param name="NextPage">Link to the next page; pass it unmodified as <c>page</c> to get the next page.</param>
public sealed record MoreEpisodesDto(IReadOnlyList<ProgrammeDto> Programmes, string? NextPage);
