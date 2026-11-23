namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// An application linked to a service or service instance (TS 103 770 clause 5.2.3, RelatedMaterial with
/// HowRelated from LinkedApplicationCS), typically an HbbTV application signalled by an XML AIT.
/// </summary>
/// <param name="HowRelated">HowRelated@href, e.g. urn:dvb:metadata:cs:LinkedApplicationCS:2019:1.2.</param>
/// <param name="Url">MediaUri, e.g. the URL of the XML AIT.</param>
/// <param name="ContentType">MediaUri@contentType, e.g. application/vnd.dvb.ait+xml.</param>
internal sealed record LinkedApplication(string HowRelated, string Url, string? ContentType);
