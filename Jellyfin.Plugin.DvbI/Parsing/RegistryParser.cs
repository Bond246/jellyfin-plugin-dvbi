using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Parsing;

/// <summary>
/// Parses the response of a service list registry query (TS 103 770 clause 5.3, ServiceListEntryPoints).
/// </summary>
internal static class RegistryParser
{
    /// <summary>
    /// Parses a ServiceListEntryPoints document.
    /// </summary>
    /// <param name="xml">XML text.</param>
    /// <returns>All service list offerings.</returns>
    /// <exception cref="DvbIParseException">Not a registry response.</exception>
    public static List<ServiceListOffering> Parse(string xml)
    {
        XElement root;
        try
        {
            root = XmlHelpers.ParseSafe(xml).Root ?? throw new DvbIParseException("Empty document");
        }
        catch (XmlException ex)
        {
            throw new DvbIParseException("Registry response is not well-formed XML: " + ex.Message, ex);
        }

        if (root.Name.LocalName != "ServiceListEntryPoints")
        {
            throw new DvbIParseException("Root element is " + root.Name.LocalName + ", expected ServiceListEntryPoints");
        }

        var result = new List<ServiceListOffering>();
        foreach (var providerOffering in root.Children("ProviderOffering"))
        {
            var provider = providerOffering.Child("Provider")?.ChildText("Name") ?? string.Empty;
            foreach (var offering in providerOffering.Children("ServiceListOffering"))
            {
                var url = offering.Children("ServiceListURI")
                    .Select(u => u.ChildText("URI"))
                    .FirstOrDefault(u => u is not null);
                if (url is null)
                {
                    continue;
                }

                result.Add(new ServiceListOffering(
                    offering.ChildText("ServiceListName") ?? url,
                    url,
                    provider,
                    offering.ChildText("ServiceListId"),
                    offering.Child("Delivery")?.Elements().Select(e => e.Name.LocalName).ToList() ?? [],
                    offering.ChildTexts("Language"),
                    offering.ChildTexts("TargetCountry"),
                    offering.BoolAttr("regulatorListFlag", false)));
            }
        }

        return result;
    }
}
