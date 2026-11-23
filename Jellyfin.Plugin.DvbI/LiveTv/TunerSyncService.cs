using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Configuration;
using Jellyfin.Plugin.DvbI.Listings;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.LiveTv;

/// <summary>
/// Keeps Jellyfin's Live TV tuner list in line with the enabled service lists of the plugin configuration.
/// Runs at startup and whenever the plugin configuration is saved.
/// </summary>
public sealed class TunerSyncService : IHostedService, IDisposable
{
    private readonly ITunerHostManager _tunerHostManager;
    private readonly IListingsManager _listingsManager;
    private readonly IConfigurationManager _configurationManager;
    private readonly ITaskManager _taskManager;
    private readonly DvbITunerHost _tunerHost;
    private readonly ILogger<TunerSyncService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="TunerSyncService"/> class.
    /// </summary>
    /// <param name="tunerHostManager">Tuner host manager.</param>
    /// <param name="listingsManager">Guide provider manager.</param>
    /// <param name="configurationManager">Server configuration.</param>
    /// <param name="taskManager">Task manager.</param>
    /// <param name="tunerHost">The DVB-I tuner host.</param>
    /// <param name="logger">Logger.</param>
    public TunerSyncService(
        ITunerHostManager tunerHostManager,
        IListingsManager listingsManager,
        IConfigurationManager configurationManager,
        ITaskManager taskManager,
        DvbITunerHost tunerHost,
        ILogger<TunerSyncService> logger)
    {
        _tunerHostManager = tunerHostManager;
        _listingsManager = listingsManager;
        _configurationManager = configurationManager;
        _taskManager = taskManager;
        _tunerHost = tunerHost;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            // Configurations saved by earlier versions may contain the TVHeadend password in plain text.
            if (Plugin.ProtectSecrets(Plugin.Instance.Configuration))
            {
                Plugin.Instance.SaveConfiguration();
                _logger.LogInformation("Encrypted the stored TVHeadend password");
            }

            Plugin.Instance.ConfigurationChanged += OnConfigurationChanged;
            await SyncAsync(Plugin.Instance.Configuration, false).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Plugin.Instance is not null)
        {
            Plugin.Instance.ConfigurationChanged -= OnConfigurationChanged;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => _lock.Dispose();

    /// <summary>
    /// Builds the tuner display name for a service list.
    /// </summary>
    /// <param name="source">Service list source.</param>
    /// <returns>Display name.</returns>
    internal static string TunerName(ServiceListSource source)
        => string.IsNullOrEmpty(source.RegionName) ? "DVB-I: " + source.Name : "DVB-I: " + source.Name + " (" + source.RegionName + ")";

    private async void OnConfigurationChanged(object? sender, BasePluginConfiguration e)
    {
        try
        {
            await SyncAsync((PluginConfiguration)e, true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Synchronising DVB-I tuners failed");
        }
    }

    private async Task SyncAsync(PluginConfiguration config, bool refreshGuide)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            _tunerHost.ClearCache();
            var existing = _configurationManager.GetConfiguration<LiveTvOptions>("livetv").TunerHosts
                .Where(t => string.Equals(t.Type, DvbITunerHost.TunerType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var wanted = config.ServiceLists.Where(s => s.Enabled && !string.IsNullOrWhiteSpace(s.Url)).ToList();
            var changed = false;

            foreach (var tuner in existing.Where(t => !wanted.Any(s => string.Equals(s.Id, t.Url, StringComparison.Ordinal))))
            {
                _logger.LogInformation("Removing DVB-I tuner {Name}", tuner.FriendlyName);
                _tunerHostManager.DeleteTunerHost(tuner.Id);
                changed = true;
            }

            foreach (var source in wanted)
            {
                var tuner = existing.FirstOrDefault(t => string.Equals(t.Url, source.Id, StringComparison.Ordinal));
                var name = TunerName(source);
                if (tuner is not null && string.Equals(tuner.FriendlyName, name, StringComparison.Ordinal))
                {
                    continue;
                }

                var info = tuner ?? new TunerHostInfo
                {
                    Type = DvbITunerHost.TunerType,
                    Url = source.Id,
                    TunerCount = 0,
                    AllowStreamSharing = true,
                    AllowFmp4TranscodingContainer = false,
                };
                info.FriendlyName = name;
                info.DeviceId = source.Id;
                _logger.LogInformation("{Action} DVB-I tuner {Name}", tuner is null ? "Adding" : "Updating", name);
                await _tunerHostManager.SaveTunerHost(info, false).ConfigureAwait(false);
                changed = true;
            }

            var (providerSaved, guideQueued) = await SyncListingsProviderAsync(refreshGuide || changed).ConfigureAwait(false);

            // Region, offset or stream preference changes do not alter the tuner entry but the channels do.
            if ((refreshGuide || changed || providerSaved) && !guideQueued)
            {
                // At server start the scheduled tasks are registered after the hosted services started.
                _ = QueueGuideRefreshAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Keeps exactly one "dvbi" guide provider, enabled only for the DVB-I tuners. Its channels have
    /// the tuner channel ids, so Jellyfin maps them automatically. Saving it also clears Jellyfin's
    /// cached guide channel list and queues a guide refresh.
    /// </summary>
    /// <returns>Whether the provider was saved and whether Jellyfin queued a guide refresh.</returns>
    private async Task<(bool Saved, bool Queued)> SyncListingsProviderAsync(bool channelsChanged)
    {
        var liveTv = _configurationManager.GetConfiguration<LiveTvOptions>("livetv");
        var tunerIds = liveTv.TunerHosts
            .Where(t => string.Equals(t.Type, DvbITunerHost.TunerType, StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var providers = liveTv.ListingProviders
            .Where(p => string.Equals(p.Type, DvbIListingsProvider.ProviderType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (tunerIds.Length == 0)
        {
            foreach (var provider in providers)
            {
                _logger.LogInformation("Removing DVB-I guide provider");
                _listingsManager.DeleteListingsProvider(provider.Id);
            }

            return (false, false);
        }

        foreach (var duplicate in providers.Skip(1))
        {
            _listingsManager.DeleteListingsProvider(duplicate.Id);
        }

        var info = providers.FirstOrDefault();
        var tunersChanged = info is null || !info.EnabledTuners.Order(StringComparer.Ordinal).SequenceEqual(tunerIds, StringComparer.Ordinal);
        if (!tunersChanged && !channelsChanged)
        {
            return (false, false);
        }

        info ??= new ListingsProviderInfo { Type = DvbIListingsProvider.ProviderType, ListingsId = "dvbi" };
        info.EnableAllTuners = false;
        info.EnabledTuners = tunerIds;
        try
        {
            await _listingsManager.SaveListingProvider(info, false, false).ConfigureAwait(false);
            _logger.LogInformation("DVB-I guide provider saved for {Count} tuner(s)", tunerIds.Length);
            return (true, true);
        }
        catch (InvalidOperationException ex)
        {
            // At server start the guide refresh task may not be registered yet; the provider is saved anyway.
            _logger.LogInformation("DVB-I guide provider saved for {Count} tuner(s), guide refresh follows when the server is ready", tunerIds.Length);
            _logger.LogDebug(ex, "Jellyfin could not queue the guide refresh yet");
            return (true, false);
        }
    }

    private async Task QueueGuideRefreshAsync()
    {
        IScheduledTaskWorker? task = null;
        for (var attempt = 0; attempt < 60 && task is null; attempt++)
        {
            task = _taskManager.ScheduledTasks.FirstOrDefault(t => string.Equals(t.ScheduledTask.Key, "RefreshGuide", StringComparison.Ordinal));
            if (task is null)
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }

        if (task is null)
        {
            _logger.LogWarning("Refresh Guide task not found, channels update with the next scheduled guide refresh");
            return;
        }

        if (task.State == TaskState.Running)
        {
            _taskManager.Cancel(task);
        }

        _taskManager.QueueScheduledTask(task.ScheduledTask, new TaskOptions());
        _logger.LogInformation("Queued Live TV guide refresh");
    }
}
