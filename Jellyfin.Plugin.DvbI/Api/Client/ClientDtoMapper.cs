using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.ServiceLists;

namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// Maps the internal DVB-I model to the client API DTOs.
/// </summary>
internal static class ClientDtoMapper
{
    /// <summary>
    /// Maps a channel.
    /// </summary>
    /// <param name="channel">Channel details.</param>
    /// <param name="jellyfinId">Jellyfin item id, if known.</param>
    /// <returns>The DTO.</returns>
    public static ChannelDto ToDto(ChannelDetails channel, Guid? jellyfinId)
    {
        var service = channel.Lineup.Service;
        var playable = channel.Lineup.Instances.ToDictionary(p => p.Instance, p => p.Tvheadend);
        var source = channel.List.GetContentGuideSource(service);
        var guide = channel.List.GetGuideSource(service);

        return new ChannelDto(
            jellyfinId,
            channel.Info.Id,
            channel.Info.Number,
            service.Name,
            service.ProviderName,
            service.Kind.ToString(),
            service.LogoUrl,
            service.UniqueIdentifier,
            channel.SourceName,
            channel.SourceUrl,
            service.Instances
                .OrderBy(i => i.Priority)
                .Select(i => new ServiceInstanceDto(
                    i.Priority,
                    i.Delivery.ToString(),
                    i.Uri?.ToString(),
                    i.DisplayName,
                    i.HasContentProtection,
                    playable.ContainsKey(i),
                    i.BroadcastParameters,
                    ToDto(i.LinkedApplications),
                    playable.GetValueOrDefault(i)?.Name))
                .ToList(),
            ToDto(service.LinkedApplications),
            source is null
                ? null
                : new GuideSourceDto(source.ScheduleInfoEndpoint, source.ProgramInfoEndpoint, source.MoreEpisodesEndpoint, guide?.ServiceId ?? service.ContentGuideServiceRef ?? service.UniqueIdentifier));
    }

    /// <summary>
    /// Maps a schedule event.
    /// </summary>
    /// <param name="e">Event.</param>
    /// <returns>The DTO.</returns>
    public static ScheduleEventDto ToDto(EpgEvent e)
        => new(
            e.Crid,
            e.StartUtc,
            e.EndUtc,
            e.Title,
            e.EpisodeTitle,
            e.Synopsis,
            e.ImageUrl,
            e.GenreIds,
            e.MinimumAge,
            e.IsHd,
            e.HasSubtitles,
            e.HasAudioDescription,
            e.HasSignLanguage,
            ToDto(e.OnDemand));

    /// <summary>
    /// Maps a programme summary.
    /// </summary>
    /// <param name="p">Programme.</param>
    /// <returns>The DTO.</returns>
    public static ProgrammeDto ToDto(ProgrammeSummary p)
        => new(p.Crid, p.Title, p.EpisodeTitle, p.Synopsis, p.ImageUrl, p.GenreIds, ToDto(p.OnDemand));

    private static OnDemandDto? ToDto(OnDemandOffer? offer)
        => offer is null
            ? null
            : new OnDemandDto(offer.ProgramUrl, offer.ContentType, offer.AuxiliaryUrl, offer.AvailableFromUtc, offer.AvailableUntilUtc, offer.IsFree);

    private static List<LinkedApplicationDto> ToDto(IEnumerable<LinkedApplication> applications)
        => applications.Select(a => new LinkedApplicationDto(a.HowRelated, a.Url, a.ContentType)).ToList();
}
