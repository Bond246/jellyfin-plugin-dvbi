using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// A programme from a More Episodes response.
/// </summary>
/// <param name="Crid">Programme CRID.</param>
/// <param name="Title">Title.</param>
/// <param name="EpisodeTitle">Episode title.</param>
/// <param name="Synopsis">Synopsis.</param>
/// <param name="ImageUrl">Image.</param>
/// <param name="GenreIds">DVB ContentSubject term ids.</param>
/// <param name="OnDemand">On-demand offer, if any.</param>
public sealed record ProgrammeDto(string Crid, string Title, string? EpisodeTitle, string? Synopsis, string? ImageUrl, IReadOnlyList<string> GenreIds, OnDemandDto? OnDemand);
