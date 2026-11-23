using System.Diagnostics.CodeAnalysis;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.DvbI.Configuration;

/// <summary>
/// Plugin configuration. Arrays are used because Jellyfin round-trips the configuration through
/// System.Text.Json (web page) and XmlSerializer (disk), which both handle arrays with setters reliably.
/// </summary>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Serializer compatibility, see class summary.")]
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the service list registry query URLs. There is no default: users configure the
    /// registry or service list of their DVB-I provider.
    /// </summary>
    public string[] RegistryUrls { get; set; } = [];

    /// <summary>
    /// Gets or sets the configured service lists.
    /// </summary>
    public ServiceListSource[] ServiceLists { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether HLS instances are preferred over DASH instances
    /// regardless of the signalled DVB-I priority. Off by default since the DASH preparation
    /// (stage 3) makes DASH reliable; the DVB-I priority then decides.
    /// </summary>
    public bool PreferHls { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether DASH manifests are rewritten for ffmpeg
    /// (live edge distance, best quality only, absolute base URL).
    /// </summary>
    public bool EnableDashRewrite { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether broadcast service instances are played through TVHeadend
    /// when TVHeadend can receive them ("prefer broadcast"); the DVB-I priority then decides between
    /// broadcast and internet streams, with internet streams as fallback.
    /// </summary>
    public bool PreferBroadcast { get; set; }

    /// <summary>
    /// Gets or sets the TVHeadend base URL, e.g. http://tvheadend.local:9981.
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Serializer compatibility.")]
    public string TvheadendUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVHeadend user name.
    /// </summary>
    public string TvheadendUsername { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVHeadend password, stored encrypted (see <see cref="SecretProtector"/>).
    /// </summary>
    public string TvheadendPassword { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TVHeadend streaming profile ("pass" = unchanged MPEG-TS).
    /// </summary>
    public string TvheadendProfile { get; set; } = "pass";

    /// <summary>
    /// Gets or sets a value indicating whether encrypted TVHeadend services are used. TVHeadend reports
    /// encryption but not whether it can descramble, so this is off by default.
    /// </summary>
    public bool TvheadendAllowEncrypted { get; set; }
}
