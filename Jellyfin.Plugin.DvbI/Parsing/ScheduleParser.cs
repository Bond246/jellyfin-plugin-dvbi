using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Parsing;

/// <summary>
/// Parses TV-Anytime schedule responses of DVB-I content guide servers (TS 103 770 clause 6.5 and 6.10,
/// ETSI TS 102 822-3-1). Like the service list parser it matches local names only.
/// </summary>
internal static class ScheduleParser
{
    private const string AudioDescriptionPurpose = "AudioPurposeCS:2007:1";
    private const string ContentSubjectScheme = ":ContentSubject:";
    private const string PaginationNext = ":pagination:next";

    /// <summary>
    /// Parses a TVAMain document.
    /// </summary>
    /// <param name="xml">XML text.</param>
    /// <returns>Schedule events in document order, with programme information merged.</returns>
    /// <exception cref="DvbIParseException">Not a TV-Anytime document.</exception>
    public static List<EpgEvent> Parse(string xml)
    {
        var description = LoadDescription(xml);
        if (description is null)
        {
            return [];
        }

        var programs = ProgramInformation(description);
        var locationTable = description.Child("ProgramLocationTable");
        var onDemand = OnDemandOffers(locationTable);

        var result = new List<EpgEvent>();
        foreach (var scheduleEvent in locationTable?.Children("Schedule").SelectMany(s => s.Children("ScheduleEvent")) ?? [])
        {
            var parsed = ParseEvent(scheduleEvent, programs);
            if (parsed is not null)
            {
                result.Add(onDemand.TryGetValue(parsed.Crid, out var offer) ? parsed with { OnDemand = offer } : parsed);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a programme list response without schedule, such as a More Episodes response (clause 6.7.3).
    /// </summary>
    /// <param name="xml">XML text.</param>
    /// <returns>Programmes in document order and the link to the next page, if any (clause 6.9).</returns>
    /// <exception cref="DvbIParseException">Not a TV-Anytime document.</exception>
    public static (List<ProgrammeSummary> Programmes, string? NextPage) ParseProgrammes(string xml)
    {
        var description = LoadDescription(xml);
        if (description is null)
        {
            return ([], null);
        }

        var onDemand = OnDemandOffers(description.Child("ProgramLocationTable"));
        var programmes = new List<ProgrammeSummary>();
        foreach (var (crid, info) in ProgramInformation(description))
        {
            var basic = info.Child("BasicDescription");
            var title = Title(basic, "main") ?? Title(basic, null);
            if (title is null)
            {
                continue;
            }

            var synopses = Synopses(basic);
            programmes.Add(new ProgrammeSummary(crid, title)
            {
                EpisodeTitle = Title(basic, "secondary"),
                Synopsis = synopses.LastOrDefault(),
                ImageUrl = ImageUrl(basic),
                GenreIds = GenreIds(basic).Distinct(StringComparer.Ordinal).ToList(),
                OnDemand = onDemand.GetValueOrDefault(crid),
            });
        }

        var next = description.Child("GroupInformationTable")?.Children("GroupInformation")
            .SelectMany(g => g.Child("BasicDescription")?.Children("RelatedMaterial") ?? [])
            .Where(m => m.Child("HowRelated")?.Attr("href")?.EndsWith(PaginationNext, StringComparison.Ordinal) ?? false)
            .Select(m => m.FirstDescendant("MediaUri")?.Value.Trim())
            .FirstOrDefault(u => !string.IsNullOrEmpty(u));

        return (programmes, next);
    }

    private static XElement? LoadDescription(string xml)
    {
        XElement root;
        try
        {
            root = XmlHelpers.ParseSafe(xml).Root ?? throw new DvbIParseException("Empty document");
        }
        catch (XmlException ex)
        {
            throw new DvbIParseException("TV-Anytime document is not well-formed XML: " + ex.Message, ex);
        }

        if (root.Name.LocalName != "TVAMain")
        {
            throw new DvbIParseException("Root element is " + root.Name.LocalName + ", expected TVAMain");
        }

        return root.Child("ProgramDescription");
    }

    private static Dictionary<string, XElement> ProgramInformation(XElement description)
    {
        // Dictionary preserves insertion order as long as nothing is removed.
        var programs = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var info in description.Child("ProgramInformationTable")?.Children("ProgramInformation") ?? [])
        {
            var id = info.Attr("programId");
            if (id is not null)
            {
                programs.TryAdd(id, info);
            }
        }

        return programs;
    }

    private static Dictionary<string, OnDemandOffer> OnDemandOffers(XElement? locationTable)
    {
        var offers = new Dictionary<string, OnDemandOffer>(StringComparer.Ordinal);
        foreach (var element in locationTable?.Children("OnDemandProgram") ?? [])
        {
            var crid = element.Child("Program")?.Attr("crid");
            var offer = ParseOnDemand(element);
            if (crid is not null && offer is not null)
            {
                offers.TryAdd(crid, offer);
            }
        }

        return offers;
    }

    private static List<string> Synopses(XElement? basic)
        => basic?.Children("Synopsis")
            .Select(s => s.Value.Trim())
            .Where(s => s.Length > 0)
            .OrderBy(s => s.Length)
            .ToList() ?? [];

    private static string? ImageUrl(XElement? basic)
        => basic?.Children("RelatedMaterial")
            .Select(m => m.FirstDescendant("MediaUri"))
            .FirstOrDefault(u => u is not null && (u.Attr("contentType")?.StartsWith("image/", StringComparison.Ordinal) ?? false))
            ?.Value.Trim();

    private static EpgEvent? ParseEvent(XElement scheduleEvent, Dictionary<string, XElement> programs)
    {
        var crid = scheduleEvent.Child("Program")?.Attr("crid");
        var startText = scheduleEvent.ChildText("PublishedStartTime");
        var durationText = scheduleEvent.ChildText("PublishedDuration");
        if (crid is null || startText is null || durationText is null
            || !DateTime.TryParse(startText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var start))
        {
            return null;
        }

        TimeSpan duration;
        try
        {
            duration = XmlConvert.ToTimeSpan(durationText);
        }
        catch (FormatException)
        {
            return null;
        }

        programs.TryGetValue(crid, out var info);
        var basic = info?.Child("BasicDescription");
        var instance = scheduleEvent.Child("InstanceDescription");

        var title = Title(basic, "main") ?? Title(instance, "epgevent") ?? Title(instance, "main") ?? Title(basic, null);
        if (title is null)
        {
            return null;
        }

        var synopses = Synopses(basic);

        var parental = basic?.Child("ParentalGuidance");
        var av = instance?.Child("AVAttributes") ?? basic?.Child("AVAttributes");
        var accessibility = av?.Child("AccessibilityAttributes");
        var verticalSize = av?.Child("VideoAttributes")?.ChildText("VerticalSize");

        return new EpgEvent(crid, start, start + duration, title)
        {
            EpisodeTitle = Title(basic, "secondary") ?? Title(instance, "secondary"),
            SeriesCrid = info?.Child("EpisodeOf")?.Attr("crid"),
            ShortSynopsis = synopses.FirstOrDefault(),
            Synopsis = synopses.LastOrDefault(),
            GenreIds = GenreIds(basic).Concat(GenreIds(instance)).Distinct(StringComparer.Ordinal).ToList(),
            MinimumAge = int.TryParse(parental?.ChildText("MinimumAge"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var age) ? age : null,
            RatingRegion = parental?.ChildText("Region"),
            ImageUrl = ImageUrl(basic),
            IsHd = int.TryParse(verticalSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lines) ? lines >= 720 : null,
            HasSubtitles = accessibility?.Child("SubtitleAttributes") is not null || av?.Child("CaptionLanguage") is not null,
            HasAudioDescription = accessibility?.Child("AudioDescriptionAttributes") is not null
                || (av?.Children("AudioAttributes").Any(a => a.Child("AudioLanguage")?.Attr("purpose")?.EndsWith(AudioDescriptionPurpose, StringComparison.Ordinal) ?? false) ?? false),
            HasSignLanguage = accessibility?.Child("SigningAttributes") is not null || av?.Child("SignLanguage") is not null
        };
    }

    private static OnDemandOffer? ParseOnDemand(XElement element)
    {
        var url = element.Child("ProgramURL");
        if (url is null || url.Value.Trim().Length == 0)
        {
            return null;
        }

        var free = element.Child("Free")?.Attr("value");
        return new OnDemandOffer(url.Value.Trim(), url.Attr("contentType"))
        {
            AuxiliaryUrl = element.ChildText("AuxiliaryURL"),
            AvailableFromUtc = ParseTime(element.ChildText("StartOfAvailability")),
            AvailableUntilUtc = ParseTime(element.ChildText("EndOfAvailability")),
            IsFree = free is null ? null : free is "true" or "1",
        };
    }

    private static DateTime? ParseTime(string? value)
        => DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time) ? time : null;

    private static string? Title(XElement? parent, string? type)
        => parent?.Children("Title")
            .FirstOrDefault(t => type is null || string.Equals(t.Attr("type") ?? "main", type, StringComparison.Ordinal))
            ?.Value.Trim() is { Length: > 0 } value ? value : null;

    private static IEnumerable<string> GenreIds(XElement? parent)
    {
        if (parent is null)
        {
            yield break;
        }

        // Main genres first, then secondary/other ones.
        foreach (var genre in parent.Children("Genre").OrderBy(g => string.Equals(g.Attr("type") ?? "main", "main", StringComparison.Ordinal) ? 0 : 1))
        {
            var href = genre.Attr("href");
            var index = href?.IndexOf(ContentSubjectScheme, StringComparison.Ordinal) ?? -1;
            if (index < 0)
            {
                continue;
            }

            // urn:dvb:metadata:cs:ContentSubject:2019:1.0 -> "1.0"
            var term = href![(index + ContentSubjectScheme.Length)..];
            var colon = term.IndexOf(':', StringComparison.Ordinal);
            if (colon >= 0 && colon + 1 < term.Length)
            {
                yield return term[(colon + 1)..];
            }
        }
    }
}
