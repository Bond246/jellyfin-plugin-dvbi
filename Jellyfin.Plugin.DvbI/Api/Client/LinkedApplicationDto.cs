namespace Jellyfin.Plugin.DvbI.Api.Client;

/// <summary>
/// An application linked to a service or instance, e.g. an HbbTV application via XML AIT.
/// </summary>
/// <param name="HowRelated">Classification term, e.g. urn:dvb:metadata:cs:LinkedApplicationCS:2019:1.2.</param>
/// <param name="Url">Application URL, e.g. the XML AIT.</param>
/// <param name="ContentType">Content type, e.g. application/vnd.dvb.ait+xml.</param>
public sealed record LinkedApplicationDto(string HowRelated, string Url, string? ContentType);
