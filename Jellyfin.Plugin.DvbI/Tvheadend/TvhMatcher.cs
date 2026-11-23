using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Tvheadend;

/// <summary>
/// Matches DVB-I broadcast service instances to TVHeadend services by DVB triplet
/// (original_network_id, transport_stream_id, service_id). DVB-I lists often omit the
/// transport_stream_id and TVHeadend reports 65536 for unknown ids, so ids are only compared when both
/// sides know them. Because service lists can contain wrong ids (seen during development: ZDFinfo and
/// zdf_neo with swapped service ids), the names must match as well (design decision: on mismatch use the internet stream).
/// </summary>
internal static class TvhMatcher
{
    /// <summary>Reason: no TVHeadend service with this triplet.</summary>
    public const string NotFound = "NotFound";

    /// <summary>Reason: the TVHeadend service has a different name.</summary>
    public const string NameMismatch = "NameMismatch";

    /// <summary>Reason: the service is scrambled and encrypted services are not allowed.</summary>
    public const string Encrypted = "Encrypted";

    /// <summary>Reason: the service is not mapped to a TVHeadend channel, so no stream ticket can be issued.</summary>
    public const string NoChannel = "NoChannel";

    private const int MinimumNameLength = 3;

    /// <summary>
    /// Matches one instance.
    /// </summary>
    /// <param name="service">The DVB-I service.</param>
    /// <param name="instance">A broadcast instance of it.</param>
    /// <param name="tvhServices">All TVHeadend services.</param>
    /// <param name="allowEncrypted">Whether scrambled services may be used.</param>
    /// <returns>The match, or null if the instance is not a broadcast instance with a triplet.</returns>
    public static TvhMatch? Match(DvbIService service, ServiceInstance instance, IReadOnlyList<TvhService> tvhServices, bool allowEncrypted)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(tvhServices);
        if (instance.Delivery is not (DeliveryKind.DvbT or DeliveryKind.DvbS or DeliveryKind.DvbC)
            || Triplet(instance) is not { } triplet)
        {
            return null;
        }

        var candidates = tvhServices
            .Where(t => t.Delivery == instance.Delivery
                && t.ServiceId == triplet.ServiceId
                && Same(t.OriginalNetworkId, triplet.OriginalNetworkId)
                && Same(t.TransportStreamId, triplet.TransportStreamId))
            .OrderByDescending(t => t.ChannelUuid is not null)
            .ThenBy(t => t.Encrypted)
            .ToList();
        if (candidates.Count == 0)
        {
            return new TvhMatch(instance, null, false, NotFound);
        }

        var names = new[] { service.Name, instance.DisplayName }.OfType<string>().ToList();
        var named = candidates.FirstOrDefault(t => names.Any(n => NamesMatch(n, t.Name)));
        if (named is null)
        {
            return new TvhMatch(instance, candidates[0], false, NameMismatch);
        }

        if (named.Encrypted && !allowEncrypted)
        {
            return new TvhMatch(instance, named, false, Encrypted);
        }

        return named.ChannelUuid is null
            ? new TvhMatch(instance, named, false, NoChannel)
            : new TvhMatch(instance, named, true, null);
    }

    /// <summary>
    /// Tolerant name comparison: case, "HD" and punctuation are ignored, and a regional suffix is
    /// allowed ("BR Fernsehen HD" matches "BR Fernsehen Nord HD"; "ZDFinfo HD" does not match "zdf_neo HD",
    /// "ZDF HD" does not match "ZDFinfo HD"). Names match if they are equal without separators or if all
    /// words of the shorter name are whole words of the longer one.
    /// </summary>
    /// <param name="a">First name.</param>
    /// <param name="b">Second name.</param>
    /// <returns>True if the names refer to the same programme.</returns>
    public static bool NamesMatch(string a, string b)
    {
        var x = Words(a);
        var y = Words(b);
        if (x.Count == 0 || y.Count == 0)
        {
            return false;
        }

        if (string.Equals(string.Concat(x), string.Concat(y), StringComparison.Ordinal))
        {
            return true;
        }

        var (shorter, longer) = x.Count <= y.Count ? (x, y) : (y, x);
        return string.Concat(shorter).Length >= MinimumNameLength && shorter.All(longer.Contains);
    }

    /// <summary>
    /// Reads the DVB triplet of a broadcast instance.
    /// </summary>
    /// <param name="instance">Instance.</param>
    /// <returns>The triplet or null.</returns>
    public static (int? OriginalNetworkId, int? TransportStreamId, int ServiceId)? Triplet(ServiceInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        var sid = Int(instance.BroadcastParameters, "DVBTriplet@serviceId");
        return sid is null
            ? null
            : (Int(instance.BroadcastParameters, "DVBTriplet@origNetId"), Int(instance.BroadcastParameters, "DVBTriplet@tsId"), sid.Value);
    }

    private static bool Same(int? a, int? b) => a is null || b is null || a == b;

    private static int? Int(IReadOnlyDictionary<string, string> values, string key)
        => values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static List<string> Words(string name)
        => name.ToLowerInvariant()
            .Split([' ', '_', '-', '.', '/', '(', ')', '&', '+', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => new string(w.Where(char.IsLetterOrDigit).ToArray()))
            .Where(w => w.Length > 0 && w is not ("hd" or "uhd" or "sd"))
            .ToList();
}
