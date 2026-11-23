using System;
using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DvbI.Configuration;

/// <summary>
/// A service list the user has added, either from a registry or by URL.
/// Each enabled list becomes one Jellyfin tuner.
/// </summary>
public class ServiceListSource
{
    /// <summary>
    /// Gets or sets the stable id. It is stored in <c>TunerHostInfo.Url</c> of the matching tuner.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the service list URL.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Serializer compatibility.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the registry the list was taken from, empty when added manually.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Serializer compatibility.")]
    public string RegistryUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the list is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the postcode last used to determine the region.
    /// </summary>
    public string Postcode { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected region id; empty means the LCN table without target region.
    /// </summary>
    public string RegionId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the selected region (for the tuner name).
    /// </summary>
    public string RegionName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the offset added to all channel numbers of this list.
    /// </summary>
    public int ChannelNumberOffset { get; set; }
}
