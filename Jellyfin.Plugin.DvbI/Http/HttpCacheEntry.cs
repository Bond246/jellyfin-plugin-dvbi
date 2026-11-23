using System;

namespace Jellyfin.Plugin.DvbI.Http;

/// <summary>
/// A cached HTTP response.
/// </summary>
/// <param name="Body">Response body.</param>
/// <param name="LastModified">Last-Modified header.</param>
/// <param name="ExpiresUtc">When max-age runs out.</param>
internal sealed record HttpCacheEntry(string Body, DateTimeOffset? LastModified, DateTime ExpiresUtc);
