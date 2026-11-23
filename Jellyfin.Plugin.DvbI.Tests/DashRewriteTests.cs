using System;
using System.Linq;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.Streaming;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class DashRewriteTests
{
    private static readonly XNamespace Ns = "urn:mpeg:dash:schema:mpd:2011";

    // Lowest quality first, no BaseURL, no suggestedPresentationDelay, 6 s segments (like arte).
    private const string ArteLike = """
        <?xml version="1.0" encoding="UTF-8"?>
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" availabilityStartTime="2026-11-18T12:08:11Z" minBufferTime="PT12S" minimumUpdatePeriod="PT1M" timeShiftBufferDepth="PT2M">
          <Location>https://cdn.example.org/live/dash.mpd</Location>
          <Period id="0" start="PT0S">
            <AdaptationSet contentType="video" mimeType="video/mp4" maxHeight="720">
              <SegmentTemplate duration="150" timescale="25" initialization="video-$Bandwidth$-init.mp4" media="video-$Bandwidth$-$Number$.mp4" startNumber="1"/>
              <Representation id="4" bandwidth="200000" codecs="avc1.4d400c" width="320" height="180"/>
              <Representation id="3" bandwidth="800000" codecs="avc1.4d401e" width="640" height="360"/>
              <Representation id="1" bandwidth="2800000" codecs="avc1.640020" width="1280" height="720"/>
            </AdaptationSet>
            <AdaptationSet contentType="audio" mimeType="audio/mp4" lang="deu">
              <SegmentTemplate duration="288000" timescale="48000" initialization="audio-init.mp4" media="audio-$Number$.mp4" startNumber="1"/>
              <Representation id="5" bandwidth="128000" codecs="mp4a.40.2"/>
            </AdaptationSet>
          </Period>
        </MPD>
        """;

    // Two alternative BaseURLs, 2 s segments, mimeType on representations (like ZDF).
    private const string ZdfLike = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" availabilityStartTime="2026-10-26T04:00:00Z" minBufferTime="PT2.000S">
          <BaseURL>https://cdn.example.org/dash/live/a/</BaseURL>
          <BaseURL>https://cdn.example.org/dash/live/b/</BaseURL>
          <Period id="0" start="PT0S">
            <AdaptationSet id="0" maxHeight="720">
              <SegmentTemplate timescale="48000" duration="96000" startNumber="0" media="x_$RepresentationID$-$Number$.mp4" initialization="x_$RepresentationID$-init.mp4"/>
              <Representation id="video_00" mimeType="video/mp4" codecs="avc1.4d401f" bandwidth="1500000" width="960" height="540"/>
              <Representation id="video_01" mimeType="video/mp4" codecs="avc1.640028" bandwidth="3200000" width="1280" height="720"/>
            </AdaptationSet>
            <AdaptationSet id="1" lang="de">
              <SegmentTemplate timescale="48000" duration="96000" startNumber="0" media="y_$RepresentationID$-$Number$.mp4" initialization="y_$RepresentationID$-init.mp4"/>
              <Representation id="audio_02" mimeType="audio/mp4" codecs="mp4a.40.2" bandwidth="128000"/>
            </AdaptationSet>
          </Period>
        </MPD>
        """;

    // Relative URLs, inherited timescale, existing delay, several audio sets and subtitles (like ARD).
    private const string ArdLike = """
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" availabilityStartTime="2026-11-16T05:53:43.650Z" suggestedPresentationDelay="PT6S" timeShiftBufferDepth="PT2H">
          <Period start="PT0S" id="1">
            <AdaptationSet mimeType="video/mp4">
              <SegmentTemplate timescale="90000" duration="180000" startNumber="1"/>
              <Representation id="1" width="1280" height="720" bandwidth="3200000" codecs="avc1.640020">
                <SegmentTemplate duration="180000" startNumber="1" media="v720-$Number$.mp4" initialization="v720-init.mp4"/>
              </Representation>
              <Representation id="2" width="960" height="540" bandwidth="1600000" codecs="avc1.4d401f">
                <SegmentTemplate duration="180000" startNumber="1" media="v540-$Number$.mp4" initialization="v540-init.mp4"/>
              </Representation>
            </AdaptationSet>
            <AdaptationSet mimeType="audio/mp4" lang="de">
              <SegmentTemplate timescale="48000" duration="96000" media="a1-$Number$.mp4" initialization="a1-init.mp4" startNumber="1"/>
              <Representation id="4" bandwidth="128000" codecs="mp4a.40.2"/>
            </AdaptationSet>
            <AdaptationSet mimeType="audio/mp4" lang="de">
              <Role schemeIdUri="urn:mpeg:dash:role:2011" value="description"/>
              <SegmentTemplate timescale="48000" duration="96000" media="a2-$Number$.mp4" initialization="a2-init.mp4" startNumber="1"/>
              <Representation id="5" bandwidth="128000" codecs="mp4a.40.2"/>
            </AdaptationSet>
            <AdaptationSet mimeType="application/mp4" lang="de">
              <SegmentTemplate timescale="1000" duration="2000" media="t-$Number$.mp4" initialization="t-init.mp4" startNumber="1"/>
              <Representation id="7" bandwidth="2000" codecs="stpp"/>
            </AdaptationSet>
          </Period>
        </MPD>
        """;

    private static readonly Uri ManifestUrl = new("https://live.example.org/channel/dash/de/manifest.mpd");

    private static XElement Rewrite(string mpd) => XDocument.Parse(DashManifestRewriter.Rewrite(mpd, ManifestUrl)).Root!;

    [Fact]
    public void KeepsOnlyBestVideoRepresentation()
    {
        var root = Rewrite(ArteLike);
        var video = root.Descendants(Ns + "AdaptationSet").Single(a => (string?)a.Attribute("contentType") == "video");
        var rep = Assert.Single(video.Elements(Ns + "Representation"));
        Assert.Equal("720", (string?)rep.Attribute("height"));
        Assert.Single(root.Descendants(Ns + "AdaptationSet"), a => (string?)a.Attribute("contentType") == "audio");
    }

    [Fact]
    public void AddsLiveDelayOfThreeSegments()
    {
        Assert.Equal("PT18S", (string?)Rewrite(ArteLike).Attribute("suggestedPresentationDelay"));
        Assert.Equal("PT6S", (string?)Rewrite(ZdfLike).Attribute("suggestedPresentationDelay"));
    }

    [Fact]
    public void LiveDelayRespectsTimeShiftBufferAndExistingValue()
    {
        // arte-like has timeShiftBufferDepth 2 min -> delay 18 s stays below 60 s; ARD keeps its 6 s.
        Assert.Equal("PT6S", (string?)Rewrite(ArdLike).Attribute("suggestedPresentationDelay"));
        var shortBuffer = ArteLike.Replace("timeShiftBufferDepth=\"PT2M\"", "timeShiftBufferDepth=\"PT20S\"", StringComparison.Ordinal);
        Assert.Equal("PT10S", (string?)Rewrite(shortBuffer).Attribute("suggestedPresentationDelay"));
    }

    [Fact]
    public void UsesMinBufferTimeWhenPresentationTimeOffsetIsSet()
    {
        var withPto = ZdfLike.Replace("startNumber=\"0\" media=\"x_", "presentationTimeOffset=\"960000\" startNumber=\"0\" media=\"x_", StringComparison.Ordinal);
        Assert.Equal("PT3S", (string?)Rewrite(withPto).Attribute("minBufferTime"));
        Assert.Equal("PT2.000S", (string?)Rewrite(ZdfLike).Attribute("minBufferTime"));
    }

    [Fact]
    public void MakesBaseUrlAbsoluteAndRemovesLocation()
    {
        var arte = Rewrite(ArteLike);
        Assert.Empty(arte.Elements(Ns + "Location"));
        Assert.Equal("https://live.example.org/channel/dash/de/", (string?)Assert.Single(arte.Elements(Ns + "BaseURL")));
        Assert.True(arte.Elements().ToList().IndexOf(arte.Element(Ns + "BaseURL")!) < arte.Elements().ToList().IndexOf(arte.Element(Ns + "Period")!));

        var zdf = Rewrite(ZdfLike);
        Assert.Equal("https://cdn.example.org/dash/live/a/", (string?)Assert.Single(zdf.Elements(Ns + "BaseURL")));

        var relative = ZdfLike.Replace("<BaseURL>https://cdn.example.org/dash/live/a/</BaseURL>", "<BaseURL>../segments/</BaseURL>", StringComparison.Ordinal);
        Assert.Equal("https://live.example.org/channel/dash/segments/", (string?)Rewrite(relative).Element(Ns + "BaseURL"));
    }

    [Fact]
    public void KeepsAudioDescriptionAndSubtitleSets()
    {
        var root = Rewrite(ArdLike);
        var sets = root.Descendants(Ns + "AdaptationSet").ToList();
        Assert.Equal(4, sets.Count);
        Assert.Single(sets[0].Elements(Ns + "Representation"));
        Assert.Equal("1", (string?)sets[0].Element(Ns + "Representation")!.Attribute("id"));
    }

    [Fact]
    public void PrefersH264AndAtMost1080Lines()
    {
        const string Mixed = """
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static">
              <Period>
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="hevc" codecs="hev1.1.6.L150" bandwidth="9000000" height="2160"/>
                </AdaptationSet>
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="uhd" codecs="avc1.640033" bandwidth="15000000" height="2160"/>
                  <Representation id="fhd" codecs="avc1.640028" bandwidth="6000000" height="1080"/>
                </AdaptationSet>
              </Period>
            </MPD>
            """;
        var root = Rewrite(Mixed);
        Assert.Equal("fhd", (string?)Assert.Single(root.Descendants(Ns + "Representation")).Attribute("id"));
        Assert.Null(root.Attribute("suggestedPresentationDelay"));
    }

    [Fact]
    public void RejectsNonManifests()
    {
        Assert.Throws<DvbIParseException>(() => DashManifestRewriter.Rewrite("<html/>", ManifestUrl));
        Assert.Throws<DvbIParseException>(() => DashManifestRewriter.Rewrite("not xml", ManifestUrl));
    }
}
