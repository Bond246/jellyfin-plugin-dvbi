using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Http;
using Jellyfin.Plugin.DvbI.Model;
using Jellyfin.Plugin.DvbI.Parsing;

namespace Jellyfin.Plugin.DvbI.ServiceLists;

/// <summary>
/// Loads registries and service lists and keeps the parsed result as long as the document does not change.
/// </summary>
public sealed class ServiceListProvider
{
    private readonly DvbIHttpClient _http;
    private readonly ConcurrentDictionary<string, (string Body, ServiceList List)> _parsed = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceListProvider"/> class.
    /// </summary>
    /// <param name="http">DVB-I HTTP client.</param>
    public ServiceListProvider(DvbIHttpClient http)
    {
        _http = http;
    }

    /// <summary>
    /// Loads and parses a service list.
    /// </summary>
    /// <param name="url">Service list URL.</param>
    /// <param name="forceRefresh">Revalidate with the server even if max-age has not expired.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The service list.</returns>
    internal async Task<ServiceList> GetServiceListAsync(string url, bool forceRefresh, CancellationToken cancellationToken)
    {
        var body = await _http.GetStringAsync(url, forceRefresh, cancellationToken).ConfigureAwait(false);
        if (_parsed.TryGetValue(url, out var entry) && ReferenceEquals(entry.Body, body))
        {
            return entry.List;
        }

        var list = ServiceListParser.Parse(body);
        _parsed[url] = (body, list);
        return list;
    }

    /// <summary>
    /// Queries a service list registry.
    /// </summary>
    /// <param name="url">Registry query URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Offered service lists.</returns>
    internal async Task<List<ServiceListOffering>> GetRegistryAsync(string url, CancellationToken cancellationToken)
    {
        var body = await _http.GetStringAsync(url, true, cancellationToken).ConfigureAwait(false);
        return RegistryParser.Parse(body);
    }
}
