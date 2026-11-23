using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.DvbI.Http;

/// <summary>
/// HTTP access to DVB-I metadata endpoints following TS 103 770 clause 4.3.2 (Cache-Control max-age,
/// If-Modified-Since) and 4.6.2 (User-Agent). Responses are cached in memory and on disk, so that a
/// temporarily unreachable server does not empty the channel list.
/// </summary>
public sealed class DvbIHttpClient : IDisposable
{
    /// <summary>
    /// DVB-I standard version announced in the User-Agent. German DVB-I service lists currently use A177r6 (schema v6).
    /// </summary>
    public const int StandardVersion = 6;

    /// <summary>
    /// Upper limit for metadata documents (service lists are a few MB, schedules much smaller).
    /// </summary>
    private const long MaxDocumentBytes = 32L * 1024 * 1024;

    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DvbIHttpClient> _logger;
    private readonly ConcurrentDictionary<string, HttpCacheEntry> _memoryCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _diskLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbIHttpClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Jellyfin's HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public DvbIHttpClient(IHttpClientFactory httpClientFactory, ILogger<DvbIHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Gets the User-Agent value according to TS 103 770 clause 4.6.2.
    /// </summary>
    public static string UserAgent { get; } = string.Format(
        CultureInfo.InvariantCulture,
        "DVB-I/A177r{0} (;Jellyfin;DVB-I-Plugin;{1};;Jellyfin;)",
        StandardVersion,
        typeof(DvbIHttpClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    private static string CacheDirectory => Path.Combine(
        Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(),
        "http-cache");

    /// <summary>
    /// Gets a metadata document, honouring the cache rules of the specification.
    /// </summary>
    /// <param name="url">Absolute URL.</param>
    /// <param name="forceRefresh">Ignore max-age and revalidate with the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document body.</returns>
    /// <exception cref="HttpRequestException">The server could not be reached and nothing is cached.</exception>
    public async Task<string> GetStringAsync(string url, bool forceRefresh, CancellationToken cancellationToken)
    {
        var cached = await GetCachedAsync(url, cancellationToken).ConfigureAwait(false);
        if (cached is not null && !forceRefresh && cached.ExpiresUtc > DateTime.UtcNow)
        {
            return cached.Body;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            if (cached?.LastModified is not null)
            {
                request.Headers.IfModifiedSince = cached.LastModified;
            }

            using var response = await _httpClientFactory.CreateClient(NamedClient.Default)
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            var expires = DateTime.UtcNow + (response.Headers.CacheControl?.MaxAge ?? DefaultMaxAge);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                var refreshed = cached with { ExpiresUtc = expires };
                await StoreAsync(url, refreshed, cancellationToken).ConfigureAwait(false);
                return refreshed.Body;
            }

            response.EnsureSuccessStatusCode();
            var body = await ReadLimitedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            await StoreAsync(url, new HttpCacheEntry(body, response.Content.Headers.LastModified, expires), cancellationToken).ConfigureAwait(false);
            return body;
        }
        catch (Exception ex) when (cached is not null && ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Fetching {Url} failed, using cached copy", url);
            return cached.Body;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _diskLock.Dispose();

    /// <summary>
    /// Reads a response body up to <see cref="MaxDocumentBytes"/>, so a misbehaving server cannot exhaust memory.
    /// </summary>
    private static async Task<string> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > MaxDocumentBytes)
        {
            throw new HttpRequestException("DVB-I document is larger than " + MaxDocumentBytes + " bytes");
        }

        var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (buffer.Length + read > MaxDocumentBytes)
                {
                    throw new HttpRequestException("DVB-I document is larger than " + MaxDocumentBytes + " bytes");
                }

                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            buffer.Position = 0;
            using var reader = new StreamReader(buffer, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static string CacheFile(string url)
        => Path.Combine(CacheDirectory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32] + ".json");

    private async Task<HttpCacheEntry?> GetCachedAsync(string url, CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(url, out var entry))
        {
            return entry;
        }

        var file = CacheFile(url);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            var stream = File.OpenRead(file);
            await using (stream.ConfigureAwait(false))
            {
                entry = await JsonSerializer.DeserializeAsync<HttpCacheEntry>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Ignoring unreadable cache file {File}", file);
            return null;
        }

        if (entry is not null)
        {
            _memoryCache[url] = entry;
        }

        return entry;
    }

    private async Task StoreAsync(string url, HttpCacheEntry entry, CancellationToken cancellationToken)
    {
        _memoryCache[url] = entry;
        await _diskLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var stream = File.Create(CacheFile(url));
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, entry, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not write cache for {Url}", url);
        }
        finally
        {
            _diskLock.Release();
        }
    }
}
