using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A parsed DVB-I service list (TS 103 770 clause 5.5.1). Only the parts the plugin uses are modelled.
/// </summary>
/// <param name="Id">ServiceList@id.</param>
/// <param name="Version">ServiceList@version.</param>
/// <param name="Name">First ServiceList.Name.</param>
/// <param name="ProviderName">First ServiceList.ProviderName.</param>
/// <param name="Regions">Top level regions of the RegionList.</param>
/// <param name="LcnTables">LCN tables.</param>
/// <param name="ContentGuideSources">Content guide sources declared in the list, keyed by CGSID.</param>
/// <param name="Services">Services (TestService elements are skipped).</param>
internal sealed record ServiceList(
    string Id,
    string Version,
    string Name,
    string ProviderName,
    IReadOnlyList<Region> Regions,
    IReadOnlyList<LcnTable> LcnTables,
    IReadOnlyDictionary<string, ContentGuideSource> ContentGuideSources,
    IReadOnlyList<DvbIService> Services)
{
    /// <summary>
    /// Gets the content guide source declared for the whole list (ServiceList.ContentGuideSource),
    /// used by services without their own source or reference.
    /// </summary>
    public ContentGuideSource? DefaultContentGuideSource { get; init; }

    /// <summary>
    /// Resolves where to get the programme guide of a service (TS 103 770 clause 5.5.2 and 6.5.2.2).
    /// </summary>
    /// <param name="service">The service.</param>
    /// <returns>The schedule endpoint and the service id to query, or null if the service has no guide.</returns>
    public (string Endpoint, string ServiceId)? GetGuideSource(DvbIService service)
    {
        var source = GetContentGuideSource(service);
        if (source?.ScheduleInfoEndpoint is null)
        {
            return null;
        }

        // ContentGuideServiceRef, when specified, takes precedence over UniqueIdentifier.
        return (source.ScheduleInfoEndpoint, service.ContentGuideServiceRef ?? service.UniqueIdentifier);
    }

    /// <summary>
    /// Gets the content guide source of a service: its own, the referenced one or the list default.
    /// </summary>
    /// <param name="service">The service.</param>
    /// <returns>The source or null.</returns>
    public ContentGuideSource? GetContentGuideSource(DvbIService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        var source = service.ContentGuideSource;
        if (source is null && service.ContentGuideSourceRef is not null)
        {
            ContentGuideSources.TryGetValue(service.ContentGuideSourceRef, out source);
        }

        return source ?? DefaultContentGuideSource;
    }
}
