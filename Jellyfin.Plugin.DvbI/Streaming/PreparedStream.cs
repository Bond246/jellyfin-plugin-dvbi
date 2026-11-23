using System;

namespace Jellyfin.Plugin.DvbI.Streaming;

/// <summary>
/// The result of <see cref="IStreamPreparer.Prepare"/>.
/// </summary>
/// <param name="InputUrl">URL ffmpeg reads.</param>
/// <param name="IsLocal">Whether the URL points to the Jellyfin server itself.</param>
/// <param name="Release">Called when the live stream is closed.</param>
internal sealed record PreparedStream(Uri InputUrl, bool IsLocal, Action Release);
