using System;
using Jellyfin.Plugin.DvbI.Model;

namespace Jellyfin.Plugin.DvbI.Streaming;

/// <summary>
/// Turns a reachable DVB-I service instance URL into the input URL Jellyfin's ffmpeg reads.
/// Stage 3 uses a manifest rewriting proxy for DASH; a full DASH client inside the plugin can replace
/// it later by implementing this interface (design decision 7).
/// </summary>
internal interface IStreamPreparer
{
    /// <summary>
    /// Prepares a stream.
    /// </summary>
    /// <param name="delivery">Delivery kind of the instance.</param>
    /// <param name="url">Instance URL after redirects.</param>
    /// <param name="userAgent">User-Agent for upstream requests.</param>
    /// <returns>The URL for ffmpeg and a handle to release when the stream closes.</returns>
    PreparedStream Prepare(DeliveryKind delivery, Uri url, string userAgent);
}
