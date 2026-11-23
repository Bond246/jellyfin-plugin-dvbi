using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DvbI.Listings;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class GuideTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static EpgEvent Event(string titleStart)
        => ScheduleParser.Parse(Fixture("schedule.xml")).Single(e => e.Title.StartsWith(titleStart, StringComparison.Ordinal));

    [Fact]
    public void Parse_ReadsEventsAndSkipsInvalidOnes()
    {
        var events = ScheduleParser.Parse(Fixture("schedule.xml"));
        Assert.Equal(["Tagesschau", "Morden im Norden (99)", "Der große Film", "Sendung für Kinder", "Nur Instanztitel"], events.Select(e => e.Title).ToArray());
    }

    [Fact]
    public void Parse_MergesProgramInformation()
    {
        var news = Event("Tagesschau");
        Assert.Equal("crid://example.org/news", news.Crid);
        Assert.Equal(new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc), news.StartUtc);
        Assert.Equal(DateTimeKind.Utc, news.StartUtc.Kind);
        Assert.Equal(new DateTime(2026, 11, 23, 19, 15, 0, DateTimeKind.Utc), news.EndUtc);
        Assert.Equal("Nachrichten", news.ShortSynopsis);
        Assert.Equal("Die Nachrichten des Tages", news.Synopsis);
        Assert.Equal(["2.1"], news.GenreIds.ToArray());
        Assert.Equal("https://img.example.org/news.jpg", news.ImageUrl);
        Assert.True(news.IsHd);
        Assert.True(news.HasSubtitles);
        Assert.True(news.HasSignLanguage);
        Assert.False(news.HasAudioDescription);
    }

    [Fact]
    public void Parse_ReadsEpisodeRatingAndAudioDescription()
    {
        var crime = Event("Morden");
        Assert.Equal("Aus gutem Hause", crime.EpisodeTitle);
        Assert.Equal(12, crime.MinimumAge);
        Assert.Equal("DE", crime.RatingRegion);
        Assert.True(crime.HasAudioDescription);
        Assert.False(crime.IsHd);
    }

    [Fact]
    public void Parse_ConvertsOffsetsToUtcAndUsesInstanceTitle()
    {
        Assert.Equal(new DateTime(2026, 11, 23, 20, 30, 0, DateTimeKind.Utc), Event("Sendung").StartUtc);
        Assert.Null(Event("Nur").Synopsis);
    }

    [Fact]
    public void Map_SetsCategoriesAndRatings()
    {
        var news = ProgramMapper.ToProgramInfo(Event("Tagesschau"), "dvbi_x", true);
        Assert.True(news.IsNews);
        Assert.False(news.IsMovie);
        Assert.Equal(["Nachrichten/Aktuelles"], news.Genres.ToArray());
        Assert.Equal("Die Nachrichten des Tages\n\nBarrierefrei: Untertitel, Gebärdensprache", news.Overview);
        Assert.Equal("dvbi_x", news.ChannelId);
        Assert.Equal("crid://example.org/news", news.ProviderIds["dvbi"]);

        var crime = ProgramMapper.ToProgramInfo(Event("Morden"), "dvbi_x", true);
        Assert.True(crime.IsSeries);
        Assert.False(crime.IsMovie);
        Assert.Equal("FSK 12", crime.OfficialRating);
        Assert.Equal(["Film/Drama", "Krimi/Thriller"], crime.Genres.ToArray());

        var movie = ProgramMapper.ToProgramInfo(Event("Der große"), "dvbi_x", false);
        Assert.True(movie.IsMovie);
        Assert.False(movie.IsSeries);
        Assert.Equal("FSK 16", movie.OfficialRating);
        Assert.Equal(["Movie/Drama", "Comedy"], movie.Genres.ToArray());

        Assert.True(ProgramMapper.ToProgramInfo(Event("Sendung"), "dvbi_x", true).IsKids);
    }

    [Fact]
    public void Parse_AttachesOnDemandOffer()
    {
        var offer = Event("Morden").OnDemand;
        Assert.NotNull(offer);
        Assert.Equal("https://apps.example.org/vod.xait?id=99&x=1", offer.ProgramUrl);
        Assert.Equal("application/vnd.dvb.ait+xml", offer.ContentType);
        Assert.Equal("https://apps.example.org/vod.xait", offer.AuxiliaryUrl);
        Assert.Equal(new DateTime(2026, 11, 20, 4, 30, 6, DateTimeKind.Utc), offer.AvailableFromUtc);
        Assert.Equal(new DateTime(2027, 11, 23, 14, 9, 0, DateTimeKind.Utc), offer.AvailableUntilUtc);
        Assert.True(offer.IsFree);
        Assert.Null(Event("Tagesschau").OnDemand);

        var program = ProgramMapper.ToProgramInfo(Event("Morden"), "c", true);
        Assert.Equal("https://apps.example.org/vod.xait?id=99&x=1", program.ProviderIds["dvbiOnDemand"]);
    }

    [Fact]
    public void Map_IdsAreStableAndUnique()
    {
        var events = ScheduleParser.Parse(Fixture("schedule.xml"));
        var ids = events.Select(e => ProgramMapper.ToProgramInfo(e, "c", true).Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(ids[0], ProgramMapper.ToProgramInfo(events[0], "c", true).Id);
    }

    [Fact]
    public void Rating_UsesRegionPrefixOutsideGermany()
        => Assert.Equal("GB-15", ProgramMapper.Rating(new EpgEvent("c", DateTime.UtcNow, DateTime.UtcNow, "t") { MinimumAge = 15, RatingRegion = "gb" }));

    [Fact]
    public void GetWindows_AlignsToThreeHoursAndUses12HourWindows()
    {
        var now = new DateTime(2026, 11, 23, 14, 20, 0, DateTimeKind.Utc);
        var windows = DvbIListingsProvider.GetWindows(now, now.AddDays(1), now);
        var first = DateTimeOffset.FromUnixTimeSeconds(windows[0].Start).UtcDateTime;
        Assert.Equal(new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Utc), first);
        Assert.All(windows, w => Assert.Equal(0, w.Start % 10_800));
        Assert.All(windows, w => Assert.Equal(DvbIListingsProvider.WindowSeconds, w.End - w.Start));
        Assert.Equal(3, windows.Count);
        Assert.True(windows[^1].End >= new DateTimeOffset(now.AddDays(1)).ToUnixTimeSeconds());
    }

    [Fact]
    public void GetWindows_StaysWithin28Days()
    {
        var now = new DateTime(2026, 11, 23, 14, 20, 0, DateTimeKind.Utc);
        var windows = DvbIListingsProvider.GetWindows(now.AddDays(-40), now.AddDays(40), now);
        var today = new DateTimeOffset(now.Date, TimeSpan.Zero).ToUnixTimeSeconds();
        Assert.True(windows[0].Start >= today - (28 * 86_400));
        Assert.True(windows[^1].End <= today + (29 * 86_400));
    }

    [Fact]
    public void ScheduleUrl_FollowsParameterOrderAndEscapesServiceId()
    {
        Assert.Equal(
            "https://epg.example.org/schedule?start=1000&end=2000&sid=tag%3Aexample.org%2C2026%3Aone",
            DvbIListingsProvider.ScheduleUrl("https://epg.example.org/schedule", "tag:example.org,2026:one", 1000, 2000));
        Assert.Equal(
            "https://epg.example.org/s.php?x=1&start=1&end=2&sid=a",
            DvbIListingsProvider.ScheduleUrl("https://epg.example.org/s.php?x=1", "a", 1, 2));
    }

    [Fact]
    public void GuideSource_PrefersContentGuideServiceRef()
    {
        var list = ServiceListParser.Parse(Fixture("servicelist_v6.xml"));
        var one = list.Services.Single(s => s.Name == "Channel One HD");
        Assert.Equal(("https://epg.example.org/schedule", "epg-one"), list.GetGuideSource(one));
        Assert.Null(list.GetGuideSource(list.Services.Single(s => s.Name == "Channel Two")));
    }
}
