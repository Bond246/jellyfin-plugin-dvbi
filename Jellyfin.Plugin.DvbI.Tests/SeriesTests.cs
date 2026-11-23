using System;
using Jellyfin.Plugin.DvbI.Listings;
using Jellyfin.Plugin.DvbI.Model;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class SeriesTests
{
    private static readonly DateTime Start = new(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Morden im Norden (99)", "Morden im Norden", 99)]
    [InlineData("Morden im Norden (100) ", "Morden im Norden", 100)]
    [InlineData("Die Rosenheim-Cops (Folge 12)", "Die Rosenheim-Cops", 12)]
    [InlineData("Großstadtrevier - Folge 7", "Großstadtrevier", 7)]
    [InlineData("Tagesschau", "Tagesschau", null)]
    [InlineData("Ein Film (2024)", "Ein Film (2024)", null)]
    [InlineData("Sturm der Liebe (4321)", "Sturm der Liebe", 4321)]
    [InlineData("Lindenstraße (Folge 1999)", "Lindenstraße", 1999)]
    [InlineData("(42)", "(42)", null)]
    [InlineData("Sport (live)", "Sport (live)", null)]
    public void Split_RemovesTrailingEpisodeNumber(string title, string expectedTitle, int? expectedNumber)
    {
        var (clean, number) = SeriesTitle.Split(title);
        Assert.Equal(expectedTitle, clean);
        Assert.Equal(expectedNumber, number);
    }

    [Fact]
    public void Episodes_ShareSeriesIdAcrossNumbersAndCase()
    {
        var a = Map(new EpgEvent("crid://a/1", Start, Start.AddMinutes(45), "Morden im Norden (99)") { GenreIds = ["1.1"] });
        var b = Map(new EpgEvent("crid://a/2", Start, Start.AddMinutes(45), "Morden  im NORDEN (100)") { GenreIds = ["1.1"] });
        Assert.Equal("Morden im Norden", a.Name);
        Assert.Equal(99, a.EpisodeNumber);
        Assert.True(a.IsSeries);
        Assert.NotNull(a.SeriesId);
        Assert.Equal(a.SeriesId, b.SeriesId);
        Assert.Equal("crid://a/1", a.ShowId);
        Assert.NotEqual(a.ShowId, b.ShowId);
    }

    [Fact]
    public void EpisodeOf_TakesPrecedenceOverTitle()
    {
        var a = Map(new EpgEvent("crid://a/1", Start, Start.AddMinutes(30), "Show A") { SeriesCrid = "crid://series/x" });
        var b = Map(new EpgEvent("crid://a/2", Start, Start.AddMinutes(30), "Show B") { SeriesCrid = "crid://series/x" });
        var c = Map(new EpgEvent("crid://a/3", Start, Start.AddMinutes(30), "Show A"));
        Assert.True(a.IsSeries);
        Assert.Equal(a.SeriesId, b.SeriesId);
        Assert.NotEqual(a.SeriesId, c.SeriesId);
    }

    [Fact]
    public void Movies_HaveNoSeriesId()
    {
        var movie = Map(new EpgEvent("crid://m/1", Start, Start.AddMinutes(100), "Der große Film") { GenreIds = ["1.4"] });
        Assert.True(movie.IsMovie);
        Assert.False(movie.IsSeries);
        Assert.Null(movie.SeriesId);
    }

    [Fact]
    public void DailyShows_GetSeriesIdForSeriesRecordings()
    {
        var news = Map(new EpgEvent("crid://n/1", Start, Start.AddMinutes(15), "Tagesschau") { GenreIds = ["2.1"] });
        Assert.NotNull(news.SeriesId);
        Assert.Equal(Map(new EpgEvent("crid://n/2", Start.AddHours(1), Start.AddHours(1).AddMinutes(15), "Tagesschau")).SeriesId, news.SeriesId);
    }

    private static MediaBrowser.Controller.LiveTv.ProgramInfo Map(EpgEvent e) => ProgramMapper.ToProgramInfo(e, "c", true);
}
