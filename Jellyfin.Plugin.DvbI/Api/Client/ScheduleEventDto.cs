using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// A scheduled programme from the DVB-I content guide.
/// </summary>
/// <param name="Crid">Programme CRID.</param>
/// <param name="Start">Start (UTC).</param>
/// <param name="End">End (UTC).</param>
/// <param name="Title">Title.</param>
/// <param name="EpisodeTitle">Episode title.</param>
/// <param name="Synopsis">Longest synopsis.</param>
/// <param name="ImageUrl">Programme image.</param>
/// <param name="GenreIds">DVB ContentSubject term ids, e.g. "1.1".</param>
/// <param name="MinimumAge">Parental guidance minimum age.</param>
/// <param name="Hd">HD video, null if unknown.</param>
/// <param name="Subtitles">Subtitles available.</param>
/// <param name="AudioDescription">Audio description available.</param>
/// <param name="SignLanguage">Sign language available.</param>
/// <param name="OnDemand">On-demand offer, if any.</param>
public sealed record ScheduleEventDto(
    string Crid,
    DateTime Start,
    DateTime End,
    string Title,
    string? EpisodeTitle,
    string? Synopsis,
    string? ImageUrl,
    IReadOnlyList<string> GenreIds,
    int? MinimumAge,
    bool? Hd,
    bool Subtitles,
    bool AudioDescription,
    bool SignLanguage,
    OnDemandDto? OnDemand);
