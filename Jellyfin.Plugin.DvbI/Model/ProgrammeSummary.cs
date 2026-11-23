using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A programme without schedule information, e.g. from a More Episodes response (TS 103 770 clause 6.7).
/// </summary>
/// <param name="Crid">Programme CRID.</param>
/// <param name="Title">Main title.</param>
internal sealed record ProgrammeSummary(string Crid, string Title)
{
    /// <summary>Gets the episode title.</summary>
    public string? EpisodeTitle { get; init; }

    /// <summary>Gets the longest synopsis.</summary>
    public string? Synopsis { get; init; }

    /// <summary>Gets the image URL.</summary>
    public string? ImageUrl { get; init; }

    /// <summary>Gets the DVB ContentSubject term ids.</summary>
    public IReadOnlyList<string> GenreIds { get; init; } = [];

    /// <summary>Gets the on-demand offer, if any.</summary>
    public OnDemandOffer? OnDemand { get; init; }
}
