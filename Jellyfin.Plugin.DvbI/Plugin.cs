using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.DvbI.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.DvbI;

/// <summary>
/// The DVB-I plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "DVB-I";

    /// <inheritdoc />
    public override string Description => "DVB-I service lists and programme guide for Live TV";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("0cd0bef6-8b6b-468b-9153-4360f27c466e");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the key file for <see cref="SecretProtector"/>. It lives next to the plugin configuration, so it
    /// survives uninstalling and reinstalling the plugin just like the (encrypted) configuration does.
    /// </summary>
    internal string SecretKeyFile => Path.Combine(ApplicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.DvbI.key");

    /// <summary>
    /// Saves the configuration; a newly entered TVHeadend password is encrypted before it is stored.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        if (configuration is PluginConfiguration config)
        {
            ProtectSecrets(config);
        }

        base.UpdateConfiguration(configuration);
    }

    /// <summary>
    /// Encrypts plain text secrets of a configuration in place.
    /// </summary>
    /// <param name="config">Configuration.</param>
    /// <returns>True if something was encrypted.</returns>
    internal static bool ProtectSecrets(PluginConfiguration config)
    {
        if (string.IsNullOrEmpty(config.TvheadendPassword) || SecretProtector.IsProtected(config.TvheadendPassword))
        {
            return false;
        }

        config.TvheadendPassword = SecretProtector.Default.Protect(config.TvheadendPassword);
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
