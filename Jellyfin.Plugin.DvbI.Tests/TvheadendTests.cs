using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using Jellyfin.Plugin.DvbI.Tvheadend;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class TvheadendTests
{
    private static readonly Dictionary<string, string> NoParameters = new(StringComparer.Ordinal);

    private static ServiceInstance Dvbt(int priority, int onid, int? tsid, int sid, string? displayName = null)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DVBTriplet@origNetId"] = onid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DVBTriplet@serviceId"] = sid.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (tsid is not null)
        {
            parameters["DVBTriplet@tsId"] = tsid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ServiceInstance(priority, DeliveryKind.DvbT, null, displayName, false, parameters);
    }

    private static ServiceInstance Dash(int priority) => new(priority, DeliveryKind.Dash, new Uri("https://cdn.example.org/" + priority + ".mpd"), null, false, NoParameters);

    private static ServiceInstance Hls(int priority) => new(priority, DeliveryKind.Hls, new Uri("https://cdn.example.org/" + priority + ".m3u8"), null, false, NoParameters);

    private static DvbIService Service(string name, params ServiceInstance[] instances)
        => new("tag:example.org,2026:" + name, name, "Provider", DvbIServiceKind.Tv, null, null, null, null, instances);

    // Data as reported by a DVB-T2 TVHeadend (tsid known on scanned muxes).
    private static readonly List<TvhService> Tvh =
    [
        new("u-ard", "Das Erste HD", 769, 8468, 259, DeliveryKind.DvbT, false, "c-ard"),
        new("u-zdf", "ZDF HD", 2001, 8468, 515, DeliveryKind.DvbT, false, "c-zdf"),
        new("u-info", "ZDFinfo HD", 2002, 8468, 515, DeliveryKind.DvbT, false, "c-info"),
        new("u-neo", "zdf_neo HD", 2003, 8468, 515, DeliveryKind.DvbT, false, "c-neo"),
        new("u-br", "BR Fernsehen HD", 801, 8468, 61697, DeliveryKind.DvbT, false, "c-br"),
        new("u-pay", "Pay TV HD", 5000, 8468, 515, DeliveryKind.DvbT, true, "c-pay"),
        new("u-nochan", "Ohne Kanal", 5001, 8468, 515, DeliveryKind.DvbT, false, null),
        new("u-sat", "Das Erste HD", 28106, 1, 1019, DeliveryKind.DvbS, false, "c-sat"),
    ];

    [Fact]
    public void Match_ByNetworkAndServiceIdWhenTsidMissing()
    {
        var match = TvhMatcher.Match(Service("Das Erste HD"), Dvbt(3, 8468, null, 769), Tvh, false)!;
        Assert.True(match.Usable);
        Assert.Equal("u-ard", match.Service!.Uuid);
    }

    [Fact]
    public void Match_ComparesTsidWhenBothKnow()
    {
        Assert.True(TvhMatcher.Match(Service("ZDF HD"), Dvbt(3, 8468, 515, 2001), Tvh, false)!.Usable);
        Assert.Equal(TvhMatcher.NotFound, TvhMatcher.Match(Service("ZDF HD"), Dvbt(3, 8468, 999, 2001), Tvh, false)!.Reason);
    }

    [Fact]
    public void Match_RejectsSwappedServiceIds()
    {
        // A service list seen during development listed ZDFinfo with service id 2003, which TVHeadend receives as zdf_neo.
        var match = TvhMatcher.Match(Service("ZDFinfo HD"), Dvbt(3, 8468, null, 2003), Tvh, false)!;
        Assert.False(match.Usable);
        Assert.Equal(TvhMatcher.NameMismatch, match.Reason);
        Assert.Equal("zdf_neo HD", match.Service!.Name);
    }

    [Fact]
    public void Match_AllowsRegionalNameVariants()
        => Assert.True(TvhMatcher.Match(Service("BR Fernsehen Nord HD"), Dvbt(3, 8468, null, 801), Tvh, false)!.Usable);

    [Fact]
    public void Match_UsesDisplayNameAsAlternative()
        => Assert.True(TvhMatcher.Match(Service("Das Erste"), Dvbt(3, 8468, null, 769, "Das Erste HD"), Tvh, false)!.Usable);

    [Fact]
    public void Match_EncryptedOnlyWhenAllowed()
    {
        Assert.Equal(TvhMatcher.Encrypted, TvhMatcher.Match(Service("Pay TV"), Dvbt(1, 8468, null, 5000), Tvh, false)!.Reason);
        Assert.True(TvhMatcher.Match(Service("Pay TV"), Dvbt(1, 8468, null, 5000), Tvh, true)!.Usable);
    }

    [Fact]
    public void Match_RequiresTvheadendChannel()
        => Assert.Equal(TvhMatcher.NoChannel, TvhMatcher.Match(Service("Ohne Kanal"), Dvbt(1, 8468, null, 5001), Tvh, false)!.Reason);

    [Fact]
    public void Match_RespectsDeliverySystem()
    {
        var sat = new ServiceInstance(1, DeliveryKind.DvbS, null, null, false, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DVBTriplet@origNetId"] = "1",
            ["DVBTriplet@tsId"] = "1019",
            ["DVBTriplet@serviceId"] = "28106",
        });
        Assert.Equal("u-sat", TvhMatcher.Match(Service("Das Erste HD"), sat, Tvh, false)!.Service!.Uuid);
        Assert.Equal(TvhMatcher.NotFound, TvhMatcher.Match(Service("Das Erste HD"), Dvbt(1, 1, 1019, 28106), Tvh, false)!.Reason);
    }

    [Fact]
    public void Match_IgnoresIpInstances()
        => Assert.Null(TvhMatcher.Match(Service("X"), Dash(4), Tvh, false));

    [Theory]
    [InlineData("ZDF HD", "ZDF", true)]
    [InlineData("zdf_neo HD", "ZDFneo", true)]
    [InlineData("ZDFinfo HD", "zdf_neo HD", false)]
    [InlineData("rbb Berlin HD", "rbb Brandenburg HD", false)]
    [InlineData("ONE HD", "phoenix HD", false)]
    [InlineData("3sat", "3sat HD", true)]
    [InlineData("ZDF HD", "ZDFinfo HD", false)]
    [InlineData("BR Fernsehen Süd HD", "BR Fernsehen HD", true)]
    [InlineData("NDR FS NDS HD", "NDR FS HH HD", false)]
    [InlineData("SWR BW HD", "SWR BW HD", true)]
    public void NamesMatch_IsTolerantButNotLoose(string a, string b, bool expected)
        => Assert.Equal(expected, TvhMatcher.NamesMatch(a, b));

    [Fact]
    public void OrderPlayable_UsesDvbIPriorityAcrossBroadcastAndIp()
    {
        var dvbt = Dvbt(3, 8468, null, 2001);
        var service = Service("ZDF HD", Hls(5), Dash(4), dvbt);
        TvhService? Resolver(DvbIService s, ServiceInstance i) => ReferenceEquals(i, dvbt) ? Tvh[1] : null;

        var ordered = ChannelLineup.OrderPlayable(service, false, Resolver);
        Assert.Equal([DeliveryKind.DvbT, DeliveryKind.Dash, DeliveryKind.Hls], ordered.Select(p => p.Instance.Delivery).ToArray());
        Assert.True(ordered[0].IsBroadcast);

        var hlsFirst = ChannelLineup.OrderPlayable(service, true, Resolver);
        Assert.Equal([DeliveryKind.DvbT, DeliveryKind.Hls, DeliveryKind.Dash], hlsFirst.Select(p => p.Instance.Delivery).ToArray());

        Assert.Equal([DeliveryKind.Dash, DeliveryKind.Hls], ChannelLineup.OrderPlayable(service, false, null).Select(p => p.Instance.Delivery).ToArray());
    }

    [Fact]
    public void Build_AddsBroadcastOnlyServicesWhenTvheadendReceivesThem()
    {
        var list = ServiceListParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "servicelist_v6.xml")));
        Assert.DoesNotContain(ChannelLineup.Build(list, null, 0, false), c => c.Service.Name == "Cable Only");

        var cable = new TvhService("u-cable", "Cable Only", 28721, 61441, 10003, DeliveryKind.DvbC, false, "c-cable");
        var lineup = ChannelLineup.Build(list, null, 0, false, (s, i) => TvhMatcher.Match(s, i, [cable], false) is { Usable: true } m ? m.Service : null);
        var channel = Assert.Single(lineup, c => c.Service.Name == "Cable Only");
        Assert.True(Assert.Single(channel.Instances).IsBroadcast);
    }

    [Theory]
    [InlineData("tvheadend.local:9981", "http://tvheadend.local:9981/")]
    [InlineData("http://user:secret@tvheadend.local:9981", "http://tvheadend.local:9981/")]
    [InlineData("https://tvh.example.org/tvh", "https://tvh.example.org/tvh/")]
    [InlineData("  ", null)]
    [InlineData("ftp://x", null)]
    public void Settings_NormaliseUrl(string url, string? expected)
        => Assert.Equal(expected, TvhSettings.Create(url, "u", "p", null)?.BaseUrl.ToString());

    [Fact]
    public void Settings_MoveCredentialsOutOfTheUrl()
    {
        var settings = TvhSettings.Create("http://user:s%40cret@tvheadend.local:9981", null, null, null)!;
        Assert.Equal("http://tvheadend.local:9981/", settings.BaseUrl.ToString());
        Assert.Equal("user", settings.Username);
        Assert.Equal("s@cret", settings.Password);
    }
}

public class DigestAuthTests
{
    [Fact]
    public void Digest_MatchesRfc2617Example()
    {
        // RFC 2617 section 3.5 example (MD5, qop=auth).
        var challenge = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Digest",
            "realm=\"testrealm@host.com\", qop=\"auth,auth-int\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"");
        var header = DigestAuthHandler.Authorization(challenge, "GET", "/dir/index.html", "Mufasa", "Circle Of Life", 1, "0a4f113b");
        Assert.NotNull(header);
        Assert.Contains("response=\"6629fae49393a05397450978507c4ef1\"", header, StringComparison.Ordinal);
        Assert.Contains("qop=auth, nc=00000001, cnonce=\"0a4f113b\"", header, StringComparison.Ordinal);
        Assert.Contains("opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"", header, StringComparison.Ordinal);
    }

    [Fact]
    public void Basic_IsSupported()
        => Assert.Equal(
            "Basic dTpw",
            DigestAuthHandler.Authorization(new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", "realm=\"x\""), "GET", "/", "u", "p", 1, "c"));
}

public class SourceNameTests
{
    private static readonly System.Collections.Generic.Dictionary<string, string> NoParameters = new(StringComparer.Ordinal);

    [Fact]
    public void SourceName_ShowsDeliveryPath()
    {
        var dash = new ServiceInstance(4, DeliveryKind.Dash, new Uri("https://x/a.mpd"), null, false, NoParameters);
        var hls = new ServiceInstance(5, DeliveryKind.Hls, new Uri("https://x/a.m3u8"), null, false, NoParameters);
        var dvbt = new ServiceInstance(3, DeliveryKind.DvbT, null, null, false, NoParameters);
        var tvh = new TvhService("u", "ZDF HD", 2001, 8468, 515, DeliveryKind.DvbT, false, "c") { DeliverySystem = "DVB-T2" };

        Assert.Equal("Internet (DASH)", Jellyfin.Plugin.DvbI.LiveTv.DvbITunerHost.SourceName(new PlayableInstance(dash, null)));
        Assert.Equal("Internet (HLS)", Jellyfin.Plugin.DvbI.LiveTv.DvbITunerHost.SourceName(new PlayableInstance(hls, null)));
        Assert.Equal("DVB-T2 (TVHeadend)", Jellyfin.Plugin.DvbI.LiveTv.DvbITunerHost.SourceName(new PlayableInstance(dvbt, tvh)));
        Assert.Equal("DVB-T (TVHeadend)", Jellyfin.Plugin.DvbI.LiveTv.DvbITunerHost.SourceName(new PlayableInstance(dvbt, tvh with { DeliverySystem = null })));
    }
}

public sealed class SecretProtectorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "dvbi-secret-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Protect_RoundTripsAndHidesPlainText()
    {
        var protector = new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(_dir);
        var stored = protector.Protect("s3cret!");
        Assert.True(Jellyfin.Plugin.DvbI.Configuration.SecretProtector.IsProtected(stored));
        Assert.DoesNotContain("s3cret", stored, StringComparison.Ordinal);
        Assert.NotEqual(stored, protector.Protect("s3cret!"));
        Assert.Equal("s3cret!", protector.Unprotect(stored));
        Assert.Equal("s3cret!", new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(_dir).Unprotect(stored));
    }

    [Fact]
    public void Protect_KeepsEmptyAndProtectedValues()
    {
        var protector = new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(_dir);
        Assert.Equal(string.Empty, protector.Protect(string.Empty));
        var stored = protector.Protect("x");
        Assert.Equal(stored, protector.Protect(stored));
        Assert.Equal("legacy-plain", protector.Unprotect("legacy-plain"));
    }

    [Fact]
    public void Unprotect_FailsSafelyWithAnotherKey()
    {
        var stored = new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(_dir).Protect("x");
        var otherDir = _dir + "-other";
        try
        {
            Assert.Null(new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(otherDir).Unprotect(stored));
            Assert.Null(new Jellyfin.Plugin.DvbI.Configuration.SecretProtector(otherDir).Unprotect("enc:v1:not-base64!"));
        }
        finally
        {
            Directory.Delete(otherDir, true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }
}
