using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A DVB-I service (clause 5.5.2).
/// </summary>
/// <param name="UniqueIdentifier">Service UniqueIdentifier.</param>
/// <param name="Name">First ServiceName.</param>
/// <param name="ProviderName">First ProviderName.</param>
/// <param name="Kind">Service kind.</param>
/// <param name="LogoUrl">URL of the service logo, if signalled.</param>
/// <param name="ContentGuideSourceRef">Referenced CGSID, if any.</param>
/// <param name="ContentGuideSource">Inline content guide source, if any.</param>
/// <param name="ContentGuideServiceRef">Service id to use in content guide queries.</param>
/// <param name="Instances">All service instances.</param>
internal sealed record DvbIService(
    string UniqueIdentifier,
    string Name,
    string ProviderName,
    DvbIServiceKind Kind,
    string? LogoUrl,
    string? ContentGuideSourceRef,
    ContentGuideSource? ContentGuideSource,
    string? ContentGuideServiceRef,
    IReadOnlyList<ServiceInstance> Instances)
{
    /// <summary>
    /// Gets the applications linked to the service (e.g. HbbTV AIT URLs).
    /// </summary>
    public IReadOnlyList<LinkedApplication> LinkedApplications { get; init; } = [];
}
