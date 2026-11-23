using Jellyfin.Plugin.DvbI.Model;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// A DVB-I channel as known to the tuner, with everything the client API exposes.
/// </summary>
/// <param name="Info">Jellyfin tuner channel.</param>
/// <param name="Lineup">Lineup entry (service, number, playable instances).</param>
/// <param name="List">The service list the channel comes from.</param>
/// <param name="SourceName">Display name of the configured service list.</param>
/// <param name="SourceUrl">URL of the configured service list.</param>
internal sealed record ChannelDetails(ChannelInfo Info, LineupChannel Lineup, ServiceList List, string SourceName, string SourceUrl);
