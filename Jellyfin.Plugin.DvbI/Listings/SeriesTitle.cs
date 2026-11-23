using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.DvbI.Listings;

/// <summary>
/// Derives series information from programme titles. German DVB-I guides carry no series ids and some
/// broadcasters put the episode number into the title ("Morden im Norden (99)"), which would make Jellyfin
/// treat every episode as its own series (series timers match on series id, otherwise on the exact title).
/// </summary>
internal static partial class SeriesTitle
{
    /// <summary>
    /// Splits a trailing episode number off a title: "Title (99)", "Title (Folge 99)", "Title - Folge 99".
    /// </summary>
    /// <param name="title">Programme title.</param>
    /// <returns>Title without the number and the episode number, if one was found.</returns>
    public static (string Title, int? EpisodeNumber) Split(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var match = TrailingEpisodeNumber().Match(title);
        if (match.Success
            && match.Groups["title"].Value.Length > 0
            && int.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && !IsYear(match))
        {
            return (match.Groups["title"].Value, number);
        }

        return (title.Trim(), null);
    }

    /// <summary>
    /// Builds a channel independent series id: from a signalled series CRID (TV-Anytime EpisodeOf) if
    /// present, otherwise from the normalised title.
    /// </summary>
    /// <param name="seriesCrid">EpisodeOf CRID or null.</param>
    /// <param name="cleanTitle">Title without episode number.</param>
    /// <returns>Series id.</returns>
    public static string SeriesId(string? seriesCrid, string cleanTitle)
    {
        var key = seriesCrid is not null
            ? "crid:" + seriesCrid
            : "title:" + Normalize(cleanTitle);
        return "dvbi_series_" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
    }

    /// <summary>
    /// "Ein Film (2024)" carries a production year, not an episode number; long running soaps use four
    /// digit episode numbers as well, so only plain numbers in the year range are treated as years.
    /// </summary>
    private static bool IsYear(Match match)
        => match.Value.Contains('(', StringComparison.Ordinal)
            && !match.Value.Contains("Folge", StringComparison.OrdinalIgnoreCase)
            && int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture) is >= 1900 and < 2100;

    private static string Normalize(string title)
        => WhiteSpace().Replace(title.Trim(), " ").ToLowerInvariant();

    [GeneratedRegex(@"^\s*(?<title>.*?)\s*(?:\((?:Folge\s+)?(?<number>\d{1,4})\)|-\s*Folge\s+(?<number>\d{1,4}))\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TrailingEpisodeNumber();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpace();
}
