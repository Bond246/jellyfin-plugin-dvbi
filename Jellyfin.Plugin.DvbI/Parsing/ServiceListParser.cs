using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Parsing;

/// <summary>
/// Parses DVB-I service lists (TS 103 770 clause 5.5) of any schema version.
/// </summary>
internal static class ServiceListParser
{
    private const string ServiceLogoHowRelated = ":1001.2";
    private const string LinkedApplicationScheme = ":LinkedApplicationCS:";
    private static readonly (string Name, DeliveryKind Kind)[] BroadcastDeliveries =
    [
        ("DVBTDeliveryParameters", DeliveryKind.DvbT),
        ("DVBSDeliveryParameters", DeliveryKind.DvbS),
        ("DVBCDeliveryParameters", DeliveryKind.DvbC)
    ];

    private static readonly Dictionary<string, string> EmptyParameters = new(StringComparer.Ordinal);

    /// <summary>
    /// Parses a service list document.
    /// </summary>
    /// <param name="xml">The XML text.</param>
    /// <returns>The service list.</returns>
    /// <exception cref="DvbIParseException">The document is not a usable service list.</exception>
    public static ServiceList Parse(string xml)
    {
        XElement root;
        try
        {
            root = XmlHelpers.ParseSafe(xml).Root ?? throw new DvbIParseException("Empty document");
        }
        catch (XmlException ex)
        {
            throw new DvbIParseException("Service list is not well-formed XML: " + ex.Message, ex);
        }

        if (root.Name.LocalName != "ServiceList")
        {
            throw new DvbIParseException("Root element is " + root.Name.LocalName + ", expected ServiceList");
        }

        var status = root.Attr("responseStatus");
        if (status is not null && !string.Equals(status, "OK", StringComparison.Ordinal))
        {
            throw new DvbIParseException("Service list server returned responseStatus " + status);
        }

        var guideSources = new Dictionary<string, ContentGuideSource>(StringComparer.Ordinal);
        var guideSourceElements = root.Children("ContentGuideSource")
            .Concat(root.Children("ContentGuideSourceList").SelectMany(l => l.Children("ContentGuideSource")));
        foreach (var element in guideSourceElements)
        {
            var source = ParseContentGuideSource(element);
            guideSources.TryAdd(source.Id, source);
        }

        var regions = root.Child("RegionList")?.Children("Region").Select(ParseRegion).ToList() ?? [];
        var lcnTables = root.Child("LCNTableList")?.Children("LCNTable").Select(ParseLcnTable).ToList() ?? [];
        var services = root.Children("Service").Select(ParseService).OfType<DvbIService>().ToList();

        var defaultGuide = root.Child("ContentGuideSource");
        return new ServiceList(
            root.Attr("id") ?? string.Empty,
            root.Attr("version") ?? string.Empty,
            root.ChildText("Name") ?? "DVB-I",
            root.ChildText("ProviderName") ?? string.Empty,
            regions,
            lcnTables,
            guideSources,
            services)
        {
            DefaultContentGuideSource = defaultGuide is null ? null : ParseContentGuideSource(defaultGuide)
        };
    }

    /// <summary>
    /// Normalises a postcode for comparison: upper case, without blanks and hyphens.
    /// </summary>
    /// <param name="postcode">Postcode.</param>
    /// <returns>Normalised postcode.</returns>
    public static string NormalizePostcode(string postcode)
        => new string(postcode.Where(c => c != ' ' && c != '-').ToArray()).ToUpperInvariant();

    private static Region ParseRegion(XElement element)
    {
        var ranges = element.Children("PostcodeRange")
            .Select(r => (From: r.Attr("from"), To: r.Attr("to")))
            .Where(r => r.From is not null && r.To is not null)
            .Select(r => (NormalizePostcode(r.From!), NormalizePostcode(r.To!)))
            .ToList();

        return new Region(
            element.Attr("regionID") ?? string.Empty,
            element.ChildText("RegionName") ?? element.Attr("regionID") ?? string.Empty,
            element.BoolAttr("selectable", true),
            element.ChildTexts("Postcode").Select(NormalizePostcode).ToList(),
            element.ChildTexts("WildcardPostcode").Select(NormalizePostcode).ToList(),
            ranges,
            element.Children("Region").Select(ParseRegion).ToList());
    }

    private static LcnTable ParseLcnTable(XElement element)
    {
        var entries = new List<LcnEntry>();
        foreach (var lcn in element.Children("LCN"))
        {
            var number = lcn.IntAttr("channelNumber");
            var serviceRef = lcn.Attr("serviceRef");
            if (number is > 0 && serviceRef is not null)
            {
                entries.Add(new LcnEntry(number.Value, serviceRef, lcn.BoolAttr("visible", true)));
            }
        }

        return new LcnTable(element.ChildTexts("TargetRegion"), entries);
    }

    private static ContentGuideSource ParseContentGuideSource(XElement element)
        => new(
            element.Attr("CGSID") ?? string.Empty,
            element.ChildText("ProviderName") ?? string.Empty,
            element.Child("ScheduleInfoEndpoint")?.FirstDescendant("URI")?.Value.Trim(),
            element.Child("ProgramInfoEndpoint")?.FirstDescendant("URI")?.Value.Trim())
        {
            MoreEpisodesEndpoint = element.Child("MoreEpisodesEndpoint")?.FirstDescendant("URI")?.Value.Trim()
        };

    private static DvbIService? ParseService(XElement element)
    {
        var id = element.ChildText("UniqueIdentifier");
        if (id is null)
        {
            return null;
        }

        var inlineGuide = element.Child("ContentGuideSource");

        return new DvbIService(
            id,
            element.ChildText("ServiceName") ?? id,
            element.ChildText("ProviderName") ?? string.Empty,
            ParseKind(element.Child("ServiceType")?.Attr("href")),
            FindLogo(element),
            element.ChildText("ContentGuideSourceRef"),
            inlineGuide is null ? null : ParseContentGuideSource(inlineGuide),
            element.ChildText("ContentGuideServiceRef"),
            element.Children("ServiceInstance").Select(ParseInstance).ToList())
        {
            LinkedApplications = FindLinkedApplications(element)
        };
    }

    private static DvbIServiceKind ParseKind(string? href)
    {
        if (href is null || href.EndsWith(":linear", StringComparison.Ordinal))
        {
            return DvbIServiceKind.Tv;
        }

        return href.EndsWith(":linear-radio", StringComparison.Ordinal) ? DvbIServiceKind.Radio : DvbIServiceKind.Other;
    }

    private static string? FindLogo(XElement service)
    {
        foreach (var material in service.Children("RelatedMaterial"))
        {
            var howRelated = material.Child("HowRelated")?.Attr("href");
            if (howRelated is not null && howRelated.EndsWith(ServiceLogoHowRelated, StringComparison.Ordinal))
            {
                var uri = material.FirstDescendant("MediaUri")?.Value.Trim();
                if (!string.IsNullOrEmpty(uri))
                {
                    return uri;
                }
            }
        }

        return null;
    }

    private static List<LinkedApplication> FindLinkedApplications(XElement parent)
    {
        var result = new List<LinkedApplication>();
        foreach (var material in parent.Children("RelatedMaterial"))
        {
            var howRelated = material.Child("HowRelated")?.Attr("href");
            var mediaUri = material.FirstDescendant("MediaUri");
            var url = mediaUri?.Value.Trim();
            if (howRelated is not null && howRelated.Contains(LinkedApplicationScheme, StringComparison.Ordinal) && !string.IsNullOrEmpty(url))
            {
                result.Add(new LinkedApplication(howRelated, url, mediaUri!.Attr("contentType")));
            }
        }

        return result;
    }

    private static ServiceInstance ParseInstance(XElement element)
        => ParseInstanceDelivery(element) with { LinkedApplications = FindLinkedApplications(element) };

    private static ServiceInstance ParseInstanceDelivery(XElement element)
    {
        var priority = element.IntAttr("priority") ?? 0;
        var displayName = element.ChildText("DisplayName");
        var protectedContent = element.Child("ContentProtection") is not null;

        var dash = element.Child("DASHDeliveryParameters");
        if (dash is not null)
        {
            return new ServiceInstance(priority, DeliveryKind.Dash, ParseUri(dash.FirstDescendant("URI")?.Value), displayName, protectedContent, EmptyParameters);
        }

        var other = element.Child("OtherDeliveryParameters");
        if (other is not null)
        {
            var location = other.FirstDescendant("UriBasedLocation");
            var contentType = location?.Attr("contentType") ?? string.Empty;
            var extension = other.Attr("extensionName") ?? string.Empty;
            var isHls = contentType.Contains("mpegurl", StringComparison.OrdinalIgnoreCase)
                || extension.Contains("mpegurl", StringComparison.OrdinalIgnoreCase);
            return new ServiceInstance(
                priority,
                isHls ? DeliveryKind.Hls : DeliveryKind.Other,
                ParseUri(other.FirstDescendant("URI")?.Value),
                displayName,
                protectedContent,
                EmptyParameters);
        }

        foreach (var (name, kind) in BroadcastDeliveries)
        {
            var parameters = element.Child(name);
            if (parameters is not null)
            {
                return new ServiceInstance(priority, kind, null, displayName, protectedContent, FlattenParameters(parameters));
            }
        }

        return new ServiceInstance(priority, DeliveryKind.Other, null, displayName, protectedContent, EmptyParameters);
    }

    private static Dictionary<string, string> FlattenParameters(XElement parameters)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var leaf in parameters.Descendants().Where(e => !e.HasElements))
        {
            var key = leaf.Name.LocalName;
            var value = leaf.Value.Trim();
            if (value.Length > 0)
            {
                result.TryAdd(key, value);
            }

            foreach (var attribute in leaf.Attributes().Where(a => !a.IsNamespaceDeclaration))
            {
                result.TryAdd(key + "@" + attribute.Name.LocalName, attribute.Value);
            }
        }

        return result;
    }

    private static Uri? ParseUri(string? value)
        => Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ? uri : null;
}
