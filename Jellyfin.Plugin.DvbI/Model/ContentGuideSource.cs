using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// Endpoints of a content guide source (clause 5.5.17). Used by the EPG stage.
/// </summary>
/// <param name="Id">ContentGuideSource@CGSID.</param>
/// <param name="ProviderName">ProviderName.</param>
/// <param name="ScheduleInfoEndpoint">URI of ScheduleInfoEndpoint.</param>
/// <param name="ProgramInfoEndpoint">URI of ProgramInfoEndpoint, if any.</param>
internal sealed record ContentGuideSource(string Id, string ProviderName, string? ScheduleInfoEndpoint, string? ProgramInfoEndpoint)
{
    /// <summary>
    /// Gets the URI of the MoreEpisodesEndpoint (clause 6.7), if any.
    /// </summary>
    public string? MoreEpisodesEndpoint { get; init; }
}
