using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A ServiceListOffering from a service list registry query (clause 5.3.5).
/// </summary>
/// <param name="Name">ServiceListName.</param>
/// <param name="Url">First ServiceListURI.</param>
/// <param name="ProviderName">Provider name.</param>
/// <param name="ServiceListId">ServiceListId.</param>
/// <param name="Deliveries">Delivery types offered (DASHDelivery, DVBSDelivery, ...).</param>
/// <param name="Languages">Languages.</param>
/// <param name="Countries">Target countries.</param>
/// <param name="RegulatorList">ServiceListOffering@regulatorListFlag.</param>
internal sealed record ServiceListOffering(
    string Name,
    string Url,
    string ProviderName,
    string? ServiceListId,
    IReadOnlyList<string> Deliveries,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Countries,
    bool RegulatorList);
