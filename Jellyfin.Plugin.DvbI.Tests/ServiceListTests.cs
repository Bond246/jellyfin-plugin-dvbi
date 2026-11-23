using System;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using Xunit;

namespace Jellyfin.Plugin.DvbI.Tests;

public class ServiceListTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static ServiceList Load() => ServiceListParser.Parse(Fixture("servicelist_v6.xml"));

    [Fact]
    public void Parse_ReadsHeaderAndSkipsTestServices()
    {
        var list = Load();
        Assert.Equal("tag:example.org,2026:testlist", list.Id);
        Assert.Equal("42", list.Version);
        Assert.Equal("Test List", list.Name);
        Assert.Equal(9, list.Services.Count);
        Assert.DoesNotContain(list.Services, s => s.UniqueIdentifier.EndsWith(":test", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_WorksWithOtherSchemaNamespaces()
    {
        var v8 = Fixture("servicelist_v6.xml")
            .Replace("urn:dvb:metadata:servicediscovery:2024", "urn:dvb:metadata:servicediscovery:2026", StringComparison.Ordinal)
            .Replace("urn:tva:metadata:2024", "urn:tva:metadata:2026", StringComparison.Ordinal);
        var list = ServiceListParser.Parse(v8);
        Assert.Equal(9, list.Services.Count);
        Assert.Equal("https://img.example.org/one.png", list.Services[0].LogoUrl);
    }

    [Fact]
    public void Parse_ReadsServiceDetails()
    {
        var one = Load().Services.Single(s => s.Name == "Channel One HD");
        Assert.Equal(DvbIServiceKind.Tv, one.Kind);
        Assert.Equal("Broadcaster A", one.ProviderName);
        Assert.Equal("https://img.example.org/one.png", one.LogoUrl);
        Assert.Equal("cgs-1", one.ContentGuideSourceRef);
        Assert.Equal("epg-one", one.ContentGuideServiceRef);
        Assert.Equal(2, one.Instances.Count);
        Assert.Equal(DeliveryKind.Dash, one.Instances[0].Delivery);
        Assert.Equal(new Uri("https://cdn.example.org/one/manifest.mpd"), one.Instances[0].Uri);

        var sat = one.Instances[1];
        Assert.Equal(DeliveryKind.DvbS, sat.Delivery);
        Assert.False(sat.IsIp);
        Assert.Equal("19.2E", sat.BroadcastParameters["OrbitalPosition"]);
        Assert.Equal("28106", sat.BroadcastParameters["DVBTriplet@serviceId"]);
    }

    [Fact]
    public void Parse_RecognisesHlsAndRadioAndProtection()
    {
        var list = Load();
        var two = list.Services.Single(s => s.Name == "Channel Two");
        Assert.Equal(DeliveryKind.Hls, two.Instances[1].Delivery);
        Assert.Equal(5, two.Instances[1].Priority);
        Assert.Equal(DvbIServiceKind.Radio, list.Services.Single(s => s.Name == "Radio One").Kind);
        Assert.True(list.Services.Single(s => s.Name == "Pay Channel").Instances[0].HasContentProtection);
        Assert.Equal("https://epg.example.org/schedule", list.ContentGuideSources["cgs-1"].ScheduleInfoEndpoint);
    }

    [Fact]
    public void Parse_ReadsLinkedApplications()
    {
        var service = Load().Services.Single(s => s.Name == "Private Channel");
        var app = Assert.Single(service.LinkedApplications);
        Assert.Equal("urn:dvb:metadata:cs:LinkedApplicationCS:2019:1.2", app.HowRelated);
        Assert.Equal("https://apps.example.org/ait/private.aitx", app.Url);
        Assert.Equal("application/vnd.dvb.ait+xml", app.ContentType);
        Assert.Equal("https://img.example.org/private.png", service.LogoUrl);

        var appInstance = service.Instances[1];
        Assert.Equal(DeliveryKind.Other, appInstance.Delivery);
        Assert.True(appInstance.HasContentProtection);
        Assert.Equal("https://apps.example.org/ait/private-instance.aitx", Assert.Single(appInstance.LinkedApplications).Url);
        Assert.Empty(service.Instances[0].LinkedApplications);
    }

    [Fact]
    public void Build_ExcludesServicesWithoutPlayableIpInstance()
        => Assert.DoesNotContain(ChannelLineup.Build(Load(), null, 0, true), c => c.Service.Name == "Private Channel");

    [Fact]
    public void Parse_RejectsDtdsAndExternalEntities()
    {
        const string Xxe = "<?xml version=\"1.0\"?><!DOCTYPE ServiceList [<!ENTITY x SYSTEM \"file:///etc/passwd\">]><ServiceList id=\"a\" version=\"1\"><Name>&x;</Name></ServiceList>";
        Assert.Throws<DvbIParseException>(() => ServiceListParser.Parse(Xxe));
        Assert.Throws<DvbIParseException>(() => RegistryParser.Parse(Xxe.Replace("ServiceList", "ServiceListEntryPoints", StringComparison.Ordinal)));
        Assert.Throws<DvbIParseException>(() => ScheduleParser.Parse(Xxe.Replace("ServiceList", "TVAMain", StringComparison.Ordinal)));
    }

    [Fact]
    public void Parse_RejectsErrorResponsesAndWrongDocuments()
    {
        Assert.Throws<DvbIParseException>(() => ServiceListParser.Parse("<ServiceList id=\"x\" version=\"1\" responseStatus=\"ERROR_INVALID_POSTCODE\"/>"));
        Assert.Throws<DvbIParseException>(() => ServiceListParser.Parse("<Other/>"));
        Assert.Throws<DvbIParseException>(() => ServiceListParser.Parse("not xml"));
    }

    [Theory]
    [InlineData("80331", "area-de_by_ob_muc")]
    [InlineData("81000", "area-de_by_ob_muc")]
    [InlineData("95444", "area-de_by_ofr")]
    [InlineData("22769", "area-de_hh")]
    [InlineData("22-769", "area-de_hh")]
    [InlineData("12345", null)]
    [InlineData("", null)]
    public void FindRegionByPostcode_ReturnsMostSpecificRegion(string postcode, string? expected)
        => Assert.Equal(expected, ChannelLineup.FindRegionByPostcode(Load(), postcode)?.Region.Id);

    [Fact]
    public void FlattenRegions_KeepsHierarchy()
    {
        var regions = ChannelLineup.FlattenRegions(Load());
        Assert.Equal(6, regions.Count);
        var muc = regions.Single(r => r.Region.Id == "area-de_by_ob_muc");
        Assert.Equal(3, muc.Depth);
        Assert.Equal(["area-de_by_ob_muc", "area-de_by_ob", "area-de_by", "area-de"], muc.SelfAndAncestorIds().ToArray());
        Assert.False(regions.Single(r => r.Region.Id == "area-de_by").Region.Selectable);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("area-de", 0)]
    [InlineData("area-de_by", 1)]
    [InlineData("area-de_by_ob_muc", 1)]
    [InlineData("area-de_hh", 2)]
    [InlineData("unknown", 0)]
    public void SelectLcnTable_FallsBackToAncestorsAndDefault(string? regionId, int expectedIndex)
    {
        var list = Load();
        Assert.Same(list.LcnTables[expectedIndex], ChannelLineup.SelectLcnTable(list, regionId));
    }

    [Fact]
    public void Build_DefaultRegion_NumbersAndFilters()
    {
        var lineup = ChannelLineup.Build(Load(), null, 0, false);
        Assert.Equal(
            ["1 Channel One HD", "2 Channel Two", "3 Regional NDR", "50 Radio One", "51 Regional BR", "52 Extra Channel"],
            lineup.Select(c => c.Number + " " + c.Service.Name).ToArray());
    }

    [Fact]
    public void Build_RegionalTable_HidesInvisibleAndAppendsUnnumbered()
    {
        var lineup = ChannelLineup.Build(Load(), "area-de_by_ob_muc", 0, false);
        Assert.Equal(
            ["1 Channel One HD", "2 Channel Two", "3 Regional BR", "5 Radio One", "6 Extra Channel"],
            lineup.Select(c => c.Number + " " + c.Service.Name).ToArray());
    }

    [Fact]
    public void Build_AppliesOffset()
    {
        var lineup = ChannelLineup.Build(Load(), "area-de_hh", 1000, false);
        Assert.Equal([1001, 1003, 1007, 1008, 1009, 1010], lineup.Select(c => c.Number).ToArray());
    }

    [Fact]
    public void Build_OrdersInstancesByPriorityOrHlsPreference()
    {
        var byPriority = ChannelLineup.Build(Load(), null, 0, false).Single(c => c.Service.Name == "Channel Two");
        Assert.Equal([DeliveryKind.Dash, DeliveryKind.Hls], byPriority.Instances.Select(i => i.Instance.Delivery).ToArray());

        var hlsFirst = ChannelLineup.Build(Load(), null, 0, true).Single(c => c.Service.Name == "Channel Two");
        Assert.Equal([DeliveryKind.Hls, DeliveryKind.Dash], hlsFirst.Instances.Select(i => i.Instance.Delivery).ToArray());
    }

    [Fact]
    public void RegistryParser_ReadsOfferings()
    {
        var offerings = RegistryParser.Parse(Fixture("registry.xml"));
        Assert.Equal(2, offerings.Count);
        Assert.Equal("PRODUCTION", offerings[0].Name);
        Assert.Equal("https://lists.example.org/service-list/v6", offerings[0].Url);
        Assert.Equal("Example Provider", offerings[0].ProviderName);
        Assert.True(offerings[0].RegulatorList);
        Assert.Equal(["DASHDelivery", "DVBSDelivery"], offerings[0].Deliveries.ToArray());
        Assert.Equal(["DEU"], offerings[0].Countries.ToArray());
        Assert.False(offerings[1].RegulatorList);
    }
}
