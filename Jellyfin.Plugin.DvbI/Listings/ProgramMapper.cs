using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.DvbI.Model;
using MediaBrowser.Controller.LiveTv;

namespace Jellyfin.Plugin.DvbI.Listings;

/// <summary>
/// Maps TV-Anytime schedule events to Jellyfin programmes.
/// Genres use the DVB ContentSubject classification scheme (TS 103 770 annex D.5, same terms as the
/// EIT content descriptor of EN 300 468), which also drives Jellyfin's guide categories.
/// </summary>
internal static class ProgramMapper
{
    private static readonly TimeSpan MinimumMovieDuration = TimeSpan.FromMinutes(75);

    private static readonly Dictionary<string, (string De, string En)> GenreNames = new(StringComparer.Ordinal)
    {
        ["1"] = ("Film/Drama", "Movie/Drama"),
        ["1.1"] = ("Krimi/Thriller", "Detective/Thriller"),
        ["1.2"] = ("Abenteuer/Western/Krieg", "Adventure/Western/War"),
        ["1.3"] = ("Science-Fiction/Fantasy/Horror", "Science fiction/Fantasy/Horror"),
        ["1.4"] = ("Komödie", "Comedy"),
        ["1.5"] = ("Soap/Melodram", "Soap/Melodrama"),
        ["1.6"] = ("Romantik", "Romance"),
        ["1.7"] = ("Klassiker/Historienfilm", "Classical/Historical drama"),
        ["1.8"] = ("Erotik", "Adult"),
        ["2"] = ("Nachrichten/Aktuelles", "News/Current affairs"),
        ["2.3"] = ("Dokumentation", "Documentary"),
        ["2.4"] = ("Diskussion/Interview", "Discussion/Interview"),
        ["3"] = ("Show", "Show"),
        ["3.1"] = ("Quiz", "Game show"),
        ["3.3"] = ("Talkshow", "Talk show"),
        ["4"] = ("Sport", "Sports"),
        ["5"] = ("Kinder/Jugend", "Children/Youth"),
        ["6"] = ("Musik", "Music"),
        ["7"] = ("Kunst/Kultur", "Arts/Culture"),
        ["8"] = ("Gesellschaft/Politik", "Social/Political"),
        ["9"] = ("Bildung/Wissenschaft", "Education/Science"),
        ["10"] = ("Freizeit/Hobby", "Leisure/Hobbies"),
    };

    /// <summary>
    /// Creates a Jellyfin programme.
    /// </summary>
    /// <param name="epgEvent">The schedule event.</param>
    /// <param name="channelId">Jellyfin channel id.</param>
    /// <param name="german">Whether to use German genre and accessibility labels.</param>
    /// <returns>The programme.</returns>
    public static ProgramInfo ToProgramInfo(EpgEvent epgEvent, string channelId, bool german)
    {
        var topLevel = epgEvent.GenreIds.Select(TopLevel).ToHashSet(StringComparer.Ordinal);
        var isDrama = topLevel.Contains("1");
        var isMovie = isDrama && epgEvent.EpisodeTitle is null && epgEvent.EndUtc - epgEvent.StartUtc >= MinimumMovieDuration;

        var (title, episodeNumber) = SeriesTitle.Split(epgEvent.Title);
        var isSeries = !isMovie && (epgEvent.EpisodeTitle is not null || isDrama || episodeNumber is not null || epgEvent.SeriesCrid is not null);

        var program = new ProgramInfo
        {
            Id = StableId(epgEvent),
            ChannelId = channelId,
            Name = title,
            EpisodeTitle = epgEvent.EpisodeTitle,
            EpisodeNumber = episodeNumber,

            // Series recordings: Jellyfin groups episodes by SeriesId and avoids recording the same
            // ShowId twice. Movies keep no series id.
            SeriesId = isMovie ? null : SeriesTitle.SeriesId(epgEvent.SeriesCrid, title),
            ShowId = epgEvent.Crid,
            ShortOverview = epgEvent.ShortSynopsis,
            Overview = AppendAccessibility(epgEvent.Synopsis, epgEvent, german),
            StartDate = epgEvent.StartUtc,
            EndDate = epgEvent.EndUtc,
            Genres = GenreLabels(epgEvent.GenreIds, german),
            OfficialRating = Rating(epgEvent),
            IsHD = epgEvent.IsHd,
            ImageUrl = epgEvent.ImageUrl,
            HasImage = epgEvent.ImageUrl is not null,
            IsMovie = isMovie,
            IsSeries = isSeries,
            IsNews = epgEvent.GenreIds.Any(g => g is "2" or "2.0" or "2.1" or "2.2"),
            IsSports = topLevel.Contains("4"),
            IsKids = topLevel.Contains("5"),
            IsEducational = topLevel.Contains("9") || epgEvent.GenreIds.Contains("2.3", StringComparer.Ordinal),
            IsLive = epgEvent.GenreIds.Contains("11.4", StringComparer.Ordinal),
        };
        program.ProviderIds["dvbi"] = epgEvent.Crid;
        if (epgEvent.OnDemand is not null)
        {
            // Kept with the programme for the client API (stage 4), e.g. HbbTV media library deep links.
            program.ProviderIds["dvbiOnDemand"] = epgEvent.OnDemand.ProgramUrl;
        }

        return program;
    }

    /// <summary>
    /// Builds the official rating string. German ratings use the "FSK 12" form Jellyfin understands.
    /// </summary>
    /// <param name="epgEvent">Event.</param>
    /// <returns>Rating or null.</returns>
    internal static string? Rating(EpgEvent epgEvent)
    {
        if (epgEvent.MinimumAge is not { } age)
        {
            return null;
        }

        var region = epgEvent.RatingRegion;
        return string.IsNullOrEmpty(region) || string.Equals(region, "DE", StringComparison.OrdinalIgnoreCase) || string.Equals(region, "DEU", StringComparison.OrdinalIgnoreCase)
            ? "FSK " + age.ToString(CultureInfo.InvariantCulture)
            : region.ToUpperInvariant() + "-" + age.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Translates ContentSubject terms into genre labels: the main category plus a known sub category.
    /// </summary>
    /// <param name="genreIds">ContentSubject term ids.</param>
    /// <param name="german">German labels.</param>
    /// <returns>Distinct labels.</returns>
    internal static List<string> GenreLabels(IEnumerable<string> genreIds, bool german)
    {
        var labels = new List<string>();
        foreach (var id in genreIds)
        {
            foreach (var key in new[] { TopLevel(id), id })
            {
                if (GenreNames.TryGetValue(key, out var names))
                {
                    var label = german ? names.De : names.En;
                    if (!labels.Contains(label, StringComparer.Ordinal))
                    {
                        labels.Add(label);
                    }
                }
            }
        }

        return labels;
    }

    private static string TopLevel(string genreId)
    {
        var dot = genreId.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? genreId : genreId[..dot];
    }

    private static string? AppendAccessibility(string? overview, EpgEvent epgEvent, bool german)
    {
        var features = new List<string>();
        if (epgEvent.HasSubtitles)
        {
            features.Add(german ? "Untertitel" : "Subtitles");
        }

        if (epgEvent.HasAudioDescription)
        {
            features.Add(german ? "Audiodeskription" : "Audio description");
        }

        if (epgEvent.HasSignLanguage)
        {
            features.Add(german ? "Gebärdensprache" : "Sign language");
        }

        if (features.Count == 0)
        {
            return overview;
        }

        var line = (german ? "Barrierefrei: " : "Accessibility: ") + string.Join(", ", features);
        return string.IsNullOrEmpty(overview) ? line : overview + "\n\n" + line;
    }

    private static string StableId(EpgEvent epgEvent)
        => "dvbi_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            epgEvent.Crid + "|" + epgEvent.StartUtc.Ticks.ToString(CultureInfo.InvariantCulture))))[..32];
}
