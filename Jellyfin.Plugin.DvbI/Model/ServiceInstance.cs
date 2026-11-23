using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.DvbI.Model;

/// <summary>
/// A service instance (clause 5.5.3).
/// </summary>
/// <param name="Priority">ServiceInstance@priority, lower value = higher priority.</param>
/// <param name="Delivery">Delivery kind.</param>
/// <param name="Uri">Stream URI for IP deliveries.</param>
/// <param name="DisplayName">First DisplayName, if any.</param>
/// <param name="HasContentProtection">Whether ContentProtection is signalled.</param>
/// <param name="BroadcastParameters">Delivery parameter values for broadcast instances (local element name → text), kept for a later hybrid stage.</param>
internal sealed record ServiceInstance(
    int Priority,
    DeliveryKind Delivery,
    Uri? Uri,
    string? DisplayName,
    bool HasContentProtection,
    IReadOnlyDictionary<string, string> BroadcastParameters)
{
    /// <summary>
    /// Gets a value indicating whether this instance can be played over IP by Jellyfin.
    /// </summary>
    public bool IsIp => Delivery is DeliveryKind.Dash or DeliveryKind.Hls && Uri is not null;

    /// <summary>
    /// Gets the applications linked to this instance (e.g. HbbTV AIT URLs). Instances that carry only an
    /// application and no delivery parameters have <see cref="DeliveryKind.Other"/>.
    /// </summary>
    public IReadOnlyList<LinkedApplication> LinkedApplications { get; init; } = [];
}
