namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// Where the programme guide of a channel comes from (TS 103 770 clause 5.5.17 and 6.5).
/// </summary>
/// <param name="ScheduleInfoEndpoint">Schedule endpoint.</param>
/// <param name="ProgramInfoEndpoint">Programme information endpoint.</param>
/// <param name="MoreEpisodesEndpoint">More Episodes endpoint.</param>
/// <param name="ServiceId">Service id used in guide requests.</param>
public sealed record GuideSourceDto(string? ScheduleInfoEndpoint, string? ProgramInfoEndpoint, string? MoreEpisodesEndpoint, string ServiceId);
