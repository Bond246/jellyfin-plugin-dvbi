using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A scheduled programme from a TV-Anytime schedule response (TS 103 770 clause 6.5), with the
/// ProgramInformation it references already merged in.
/// </summary>
/// <param name="Crid">Programme CRID (ScheduleEvent.Program@crid).</param>
/// <param name="StartUtc">PublishedStartTime.</param>
/// <param name="EndUtc">PublishedStartTime + PublishedDuration.</param>
/// <param name="Title">Main title.</param>
internal sealed record EpgEvent(string Crid, DateTime StartUtc, DateTime EndUtc, string Title)
{
    /// <summary>Gets the secondary (episode) title.</summary>
    public string? EpisodeTitle { get; init; }

    /// <summary>Gets the shortest synopsis.</summary>
    public string? ShortSynopsis { get; init; }

    /// <summary>Gets the longest synopsis.</summary>
    public string? Synopsis { get; init; }

    /// <summary>Gets the DVB ContentSubject term ids (e.g. "1.0", "4"), main genres first.</summary>
    public IReadOnlyList<string> GenreIds { get; init; } = [];

    /// <summary>Gets the minimum age from ParentalGuidance.</summary>
    public int? MinimumAge { get; init; }

    /// <summary>Gets the region of the parental rating, if signalled.</summary>
    public string? RatingRegion { get; init; }

    /// <summary>Gets the programme image URL.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Gets a value indicating whether the video is HD (720 lines or more), null if unknown.</summary>
    public bool? IsHd { get; init; }

    /// <summary>Gets a value indicating whether subtitles are available.</summary>
    public bool HasSubtitles { get; init; }

    /// <summary>Gets a value indicating whether audio description is available.</summary>
    public bool HasAudioDescription { get; init; }

    /// <summary>Gets a value indicating whether sign language is available.</summary>
    public bool HasSignLanguage { get; init; }

    /// <summary>Gets the CRID of the series the programme is an episode of (TV-Anytime EpisodeOf), if signalled.</summary>
    public string? SeriesCrid { get; init; }

    /// <summary>Gets the on-demand offer for this programme, if any.</summary>
    public OnDemandOffer? OnDemand { get; init; }
}
