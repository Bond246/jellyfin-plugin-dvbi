using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Parsing;

namespace Jellyfin.Plugin.DvbI.Streaming;

/// <summary>
/// Rewrites live DASH manifests so that ffmpeg's DASH demuxer (used by Jellyfin) plays them reliably:
/// <list type="bullet">
/// <item>Live edge: ffmpeg starts at <c>(now - availabilityStartTime - suggestedPresentationDelay) / duration</c>.
/// Without a suggestedPresentationDelay it requests segments that are not yet available (404, e.g. arte, ZDF),
/// so a safety distance of several segments is written into the manifest.</item>
/// <item>Quality: ffmpeg opens every representation and Jellyfin uses the first video stream, which is the
/// lowest quality on some services (arte: 320x180). Only the best video representation (H.264 preferred,
/// up to 1080 lines) and the best representation per audio/text adaptation set are kept.</item>
/// <item>URLs: the manifest is served from the plugin, so the base URL is made absolute; only the first of
/// several alternative BaseURLs is kept and Location elements are removed so refreshes go through the plugin.</item>
/// </list>
/// </summary>
internal static class DashManifestRewriter
{
    /// <summary>Distance to the live edge in segments.</summary>
    internal const int LiveDelaySegments = 3;

    private const int MaxVideoHeight = 1080;

    /// <summary>Minimum distance to the live edge.</summary>
    internal static readonly TimeSpan MinimumLiveDelay = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Rewrites a manifest.
    /// </summary>
    /// <param name="mpd">Manifest XML.</param>
    /// <param name="manifestUrl">URL the manifest was loaded from (after redirects).</param>
    /// <returns>The rewritten manifest.</returns>
    /// <exception cref="DvbIParseException">Not a DASH manifest.</exception>
    public static string Rewrite(string mpd, Uri manifestUrl)
    {
        ArgumentNullException.ThrowIfNull(manifestUrl);
        XDocument document;
        try
        {
            document = XmlHelpers.ParseSafe(mpd);
        }
        catch (XmlException ex)
        {
            throw new DvbIParseException("DASH manifest is not well-formed XML: " + ex.Message, ex);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "MPD")
        {
            throw new DvbIParseException("Not a DASH manifest");
        }

        var ns = root.Name.Namespace;
        root.Elements(ns + "Location").Remove();
        FixBaseUrls(root, ns, manifestUrl);

        foreach (var period in root.Elements(ns + "Period"))
        {
            SelectRepresentations(period, ns);
        }

        if (string.Equals(root.Attribute("type")?.Value, "dynamic", StringComparison.Ordinal))
        {
            SetLiveDelay(root, ns);
        }

        return document.Declaration is null
            ? document.ToString(SaveOptions.DisableFormatting)
            : document.Declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    private static void FixBaseUrls(XElement root, XNamespace ns, Uri manifestUrl)
    {
        var baseUrls = root.Elements(ns + "BaseURL").ToList();
        if (baseUrls.Count == 0)
        {
            var element = new XElement(ns + "BaseURL", new Uri(manifestUrl, ".").ToString());
            var firstPeriod = root.Element(ns + "Period");
            if (firstPeriod is null)
            {
                root.Add(element);
            }
            else
            {
                firstPeriod.AddBeforeSelf(element);
            }
        }
        else
        {
            baseUrls[0].Value = new Uri(manifestUrl, baseUrls[0].Value.Trim()).ToString();
            foreach (var extra in baseUrls.Skip(1))
            {
                extra.Remove();
            }
        }

        // Alternative BaseURLs on lower levels: keep the first one only.
        foreach (var parent in root.Descendants().Where(e => e.Elements(ns + "BaseURL").Skip(1).Any()).ToList())
        {
            foreach (var extra in parent.Elements(ns + "BaseURL").Skip(1).ToList())
            {
                extra.Remove();
            }
        }
    }

    private static void SelectRepresentations(XElement period, XNamespace ns)
    {
        var sets = period.Elements(ns + "AdaptationSet").ToList();
        var video = sets
            .SelectMany(set => set.Elements(ns + "Representation").Select(rep => (Set: set, Rep: rep)))
            .Where(x => ContentType(x.Set, x.Rep, ns) == "video")
            .ToList();

        if (video.Count > 0)
        {
            var best = video
                .OrderByDescending(x => Codecs(x.Set, x.Rep).StartsWith("avc", StringComparison.Ordinal))
                .ThenByDescending(x => (IntAttr(x.Rep, "height") ?? IntAttr(x.Set, "maxHeight") ?? 0) <= MaxVideoHeight)
                .ThenByDescending(x => IntAttr(x.Rep, "bandwidth") ?? 0)
                .First();

            foreach (var set in video.Select(x => x.Set).Distinct().Where(s => s != best.Set).ToList())
            {
                set.Remove();
            }

            foreach (var rep in best.Set.Elements(ns + "Representation").Where(r => r != best.Rep).ToList())
            {
                rep.Remove();
            }
        }

        // Audio and text: keep every adaptation set (languages, audio description, subtitles),
        // but only its best representation.
        foreach (var set in sets.Where(s => s.Parent is not null))
        {
            var reps = set.Elements(ns + "Representation").ToList();
            if (reps.Count < 2 || reps.Any(r => ContentType(set, r, ns) == "video"))
            {
                continue;
            }

            var keep = reps.OrderByDescending(r => IntAttr(r, "bandwidth") ?? 0).First();
            foreach (var rep in reps.Where(r => r != keep))
            {
                rep.Remove();
            }
        }
    }

    private static void SetLiveDelay(XElement root, XNamespace ns)
    {
        var templates = root.Descendants(ns + "Representation")
            .Select(rep => SegmentInfo(rep, ns))
            .Where(t => t.Duration > TimeSpan.Zero)
            .ToList();

        // With SegmentTimeline ffmpeg starts 60 s before the end of the timeline, which is safe already.
        if (templates.Count == 0)
        {
            return;
        }

        var segment = templates.Max(t => t.Duration);
        var delay = TimeSpan.FromTicks(Math.Max(MinimumLiveDelay.Ticks, segment.Ticks * LiveDelaySegments));
        var existing = ParseDuration(root.Attribute("suggestedPresentationDelay")?.Value);
        if (existing is not null && existing >= delay)
        {
            delay = existing.Value;
        }

        var timeShift = ParseDuration(root.Attribute("timeShiftBufferDepth")?.Value);
        if (timeShift is not null && delay > timeShift.Value / 2)
        {
            delay = timeShift.Value / 2;
        }

        root.SetAttributeValue("suggestedPresentationDelay", XmlConvert.ToString(TimeSpan.FromSeconds(Math.Ceiling(delay.TotalSeconds))));

        // With presentationTimeOffset ffmpeg ignores suggestedPresentationDelay and subtracts minBufferTime
        // (in whole seconds) as a number of segments instead.
        if (templates.Any(t => t.HasPresentationTimeOffset))
        {
            var segments = (int)Math.Ceiling(delay / segment);
            root.SetAttributeValue("minBufferTime", "PT" + segments.ToString(CultureInfo.InvariantCulture) + "S");
        }
    }

    /// <summary>
    /// Effective SegmentTemplate values of a representation (attributes are inherited from the
    /// adaptation set and period, the most specific level wins).
    /// </summary>
    private static (TimeSpan Duration, bool HasPresentationTimeOffset) SegmentInfo(XElement representation, XNamespace ns)
    {
        var levels = new[] { representation, representation.Parent, representation.Parent?.Parent }
            .Select(e => e?.Element(ns + "SegmentTemplate"))
            .OfType<XElement>()
            .ToList();
        if (levels.Count == 0 || levels.Any(t => t.Element(ns + "SegmentTimeline") is not null))
        {
            return (TimeSpan.Zero, false);
        }

        string? Inherited(string name) => levels.Select(t => t.Attribute(name)?.Value).FirstOrDefault(v => v is not null);

        var duration = double.TryParse(Inherited("duration"), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        var timescale = double.TryParse(Inherited("timescale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && t > 0 ? t : 1;
        var pto = long.TryParse(Inherited("presentationTimeOffset"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p != 0;
        return (TimeSpan.FromSeconds(duration / timescale), pto);
    }

    private static string ContentType(XElement set, XElement rep, XNamespace ns)
    {
        var value = set.Attribute("contentType")?.Value
            ?? rep.Attribute("mimeType")?.Value
            ?? set.Attribute("mimeType")?.Value
            ?? rep.Element(ns + "ContentComponent")?.Attribute("contentType")?.Value
            ?? string.Empty;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        var type = slash < 0 ? value : value[..slash];
        if (type.Length == 0 && (rep.Attribute("height") is not null || rep.Attribute("width") is not null))
        {
            type = "video";
        }

        return type;
    }

    private static string Codecs(XElement set, XElement rep)
        => rep.Attribute("codecs")?.Value ?? set.Attribute("codecs")?.Value ?? string.Empty;

    private static int? IntAttr(XElement element, string name)
        => int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
