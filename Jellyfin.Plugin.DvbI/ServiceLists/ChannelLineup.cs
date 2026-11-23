using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.Tvheadend;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// Region lookup and channel number assignment for a service list.
/// </summary>
internal static class ChannelLineup
{
    /// <summary>
    /// Flattens the region tree in document order (depth first).
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <returns>All regions with parent links.</returns>
    public static List<RegionNode> FlattenRegions(ServiceList list)
    {
        var result = new List<RegionNode>();
        void Walk(Region region, RegionNode? parent, int depth)
        {
            var node = new RegionNode(region, parent, depth);
            result.Add(node);
            foreach (var child in region.Children)
            {
                Walk(child, node, depth + 1);
            }
        }

        foreach (var region in list.Regions)
        {
            Walk(region, null, 0);
        }

        return result;
    }

    /// <summary>
    /// Finds the most specific region that contains the postcode.
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <param name="postcode">Postcode as entered by the user.</param>
    /// <returns>The region or null when no region matches.</returns>
    public static RegionNode? FindRegionByPostcode(ServiceList list, string postcode)
    {
        var normalized = ServiceListParser.NormalizePostcode(postcode);
        if (normalized.Length == 0)
        {
            return null;
        }

        return FlattenRegions(list)
            .Where(n => Matches(n.Region, normalized))
            .OrderByDescending(n => n.Depth)
            .FirstOrDefault();
    }

    /// <summary>
    /// Selects the LCN table for a region: exact region, then its ancestors, then the table without target region.
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <param name="regionId">Selected region id or null for "no region".</param>
    /// <returns>The LCN table or null if the list has none that applies.</returns>
    public static LcnTable? SelectLcnTable(ServiceList list, string? regionId)
    {
        if (!string.IsNullOrEmpty(regionId))
        {
            var node = FlattenRegions(list).FirstOrDefault(n => string.Equals(n.Region.Id, regionId, StringComparison.Ordinal));
            if (node is not null)
            {
                foreach (var id in node.SelfAndAncestorIds())
                {
                    var table = list.LcnTables.FirstOrDefault(t => t.TargetRegions.Contains(id, StringComparer.Ordinal));
                    if (table is not null)
                    {
                        return table;
                    }
                }
            }
        }

        return list.LcnTables.FirstOrDefault(t => t.TargetRegions.Count == 0);
    }

    /// <summary>
    /// Builds the Jellyfin channel lineup for a service list.
    /// Only TV and radio services with at least one playable, unprotected IP instance are included.
    /// </summary>
    /// <param name="list">Service list.</param>
    /// <param name="regionId">Selected region.</param>
    /// <param name="numberOffset">Offset added to all channel numbers.</param>
    /// <param name="preferHls">Whether HLS instances are tried before DASH instances.</param>
    /// <param name="broadcast">Resolves broadcast instances to TVHeadend services; null when broadcast is not used.</param>
    /// <returns>Channels ordered by number.</returns>
    public static List<LineupChannel> Build(ServiceList list, string? regionId, int numberOffset, bool preferHls, Func<DvbIService, ServiceInstance, TvhService?>? broadcast = null)
    {
        var table = SelectLcnTable(list, regionId);
        var numbers = new Dictionary<string, LcnEntry>(StringComparer.Ordinal);
        foreach (var entry in table?.Entries ?? [])
        {
            numbers.TryAdd(entry.ServiceRef, entry);
        }

        var nextFree = numbers.Count == 0 ? 1 : numbers.Values.Max(e => e.ChannelNumber) + 1;
        var result = new List<LineupChannel>();
        foreach (var service in list.Services)
        {
            if (service.Kind == DvbIServiceKind.Other)
            {
                continue;
            }

            var instances = OrderPlayable(service, preferHls, broadcast);
            if (instances.Count == 0)
            {
                continue;
            }

            int number;
            if (numbers.TryGetValue(service.UniqueIdentifier, out var lcn))
            {
                if (!lcn.Visible)
                {
                    continue;
                }

                number = lcn.ChannelNumber;
            }
            else
            {
                number = nextFree++;
            }

            result.Add(new LineupChannel(service, number + numberOffset, instances));
        }

        return result.OrderBy(c => c.Number).ToList();
    }

    /// <summary>
    /// Orders the playable instances by DVB-I priority (lower value first, clause 5.5.3). IP instances with
    /// content protection are dropped because Jellyfin cannot decrypt them; broadcast instances are playable
    /// when TVHeadend receives them. With <paramref name="preferHls"/> HLS instances move before DASH
    /// instances, broadcast instances keep their position.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <param name="preferHls">Whether HLS goes before DASH.</param>
    /// <param name="broadcast">TVHeadend resolver or null.</param>
    /// <returns>Ordered playable instances.</returns>
    public static List<PlayableInstance> OrderPlayable(DvbIService service, bool preferHls, Func<DvbIService, ServiceInstance, TvhService?>? broadcast)
    {
        ArgumentNullException.ThrowIfNull(service);
        var playable = new List<PlayableInstance>();
        foreach (var instance in service.Instances.OrderBy(i => i.Priority))
        {
            if (instance.IsIp && !instance.HasContentProtection)
            {
                playable.Add(new PlayableInstance(instance, null));
            }
            else if (broadcast?.Invoke(service, instance) is { } tvh)
            {
                playable.Add(new PlayableInstance(instance, tvh));
            }
        }

        if (preferHls)
        {
            var ipSlots = playable.Select((p, index) => (p, index)).Where(x => !x.p.IsBroadcast).Select(x => x.index).ToList();
            var ipOrdered = playable.Where(p => !p.IsBroadcast).OrderBy(p => p.Instance.Delivery == DeliveryKind.Hls ? 0 : 1).ToList();
            for (var i = 0; i < ipSlots.Count; i++)
            {
                playable[ipSlots[i]] = ipOrdered[i];
            }
        }

        return playable;
    }

    private static bool Matches(Region region, string postcode)
    {
        if (region.Postcodes.Contains(postcode, StringComparer.Ordinal))
        {
            return true;
        }

        foreach (var (from, to) in region.PostcodeRanges)
        {
            if (string.CompareOrdinal(postcode, from) >= 0 && string.CompareOrdinal(postcode, to) <= 0)
            {
                return true;
            }
        }

        return region.WildcardPostcodes.Any(w => WildcardToRegex(w).IsMatch(postcode));
    }

    private static Regex WildcardToRegex(string wildcard)
        => new("^" + Regex.Escape(wildcard).Replace("\\*", "[A-Z0-9]+", StringComparison.Ordinal) + "$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
}
