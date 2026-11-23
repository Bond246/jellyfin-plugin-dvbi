using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DvbI.Listings;
using Jellyfin.Plugin.DvbI.Parsing;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class ClientApiTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    [Fact]
    public void ParseProgrammes_ReadsEpisodesOnDemandAndNextPage()
    {
        var (programmes, next) = ScheduleParser.ParseProgrammes(Fixture("more_episodes.xml"));
        Assert.Equal(["Folge 2", "Folge 1"], programmes.Select(p => p.EpisodeTitle ?? string.Empty).ToArray());
        Assert.Equal("https://img.example.org/2.png", programmes[0].ImageUrl);
        Assert.Equal(["3"], programmes[0].GenreIds.ToArray());
        Assert.Equal("https://apps.example.org/aitx?crid=crid://example.org/ep/2", programmes[0].OnDemand?.ProgramUrl);
        Assert.True(programmes[0].OnDemand?.IsFree);
        Assert.Null(programmes[1].OnDemand);
        Assert.Equal("https://epg.example.org/more/?pid=crid%3A%2F%2Fexample.org%2Fep%2F2&type=ondemand&page=1", next);
    }

    [Fact]
    public void ParseProgrammes_LastPageHasNoNext()
    {
        var xml = Fixture("more_episodes.xml").Replace("pagination:next", "pagination:prev", StringComparison.Ordinal);
        Assert.Null(ScheduleParser.ParseProgrammes(xml).NextPage);
    }

    [Fact]
    public void MoreEpisodesUrl_BuildsFirstRequest()
        => Assert.Equal(
            "https://epg.example.org/more/?pid=crid%3A%2F%2Fexample.org%2Fep%2F2&type=ondemand",
            DvbIListingsProvider.MoreEpisodesUrl("https://epg.example.org/more/", "crid://example.org/ep/2", null));

    [Theory]
    [InlineData("https://epg.example.org/more/?page=1", "https://epg.example.org/more/?page=1")]
    [InlineData("https://EPG.example.org/other/path?page=1", "https://EPG.example.org/other/path?page=1")]
    [InlineData("https://evil.example.org/more/?page=1", null)]
    [InlineData("http://epg.example.org/more/?page=1", null)]
    [InlineData("https://epg.example.org:8443/more/", null)]
    [InlineData("file:///etc/passwd", null)]
    [InlineData("not a url", null)]
    public void MoreEpisodesUrl_OnlyFollowsLinksToTheSameServer(string page, string? expected)
        => Assert.Equal(expected, DvbIListingsProvider.MoreEpisodesUrl("https://epg.example.org/more/", "c", page));

    [Fact]
    public void MoreEpisodesUrl_NullWithoutEndpoint()
        => Assert.Null(DvbIListingsProvider.MoreEpisodesUrl(null, "c", null));
}
