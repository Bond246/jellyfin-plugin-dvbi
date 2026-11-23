using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.DvbI.LiveTv;

/// <summary>
/// A live stream whose media source points directly at the (redirect-resolved) DVB-I stream URL.
/// ffmpeg reads the URL itself, so there is no local buffer and <see cref="GetStream"/> is not used.
/// </summary>
internal sealed class DvbILiveStream : ILiveStream
{
    private Action? _onClose;

    /// <summary>
    /// Initializes a new instance of the <see cref="DvbILiveStream"/> class.
    /// </summary>
    /// <param name="mediaSource">Media source.</param>
    /// <param name="tunerHostId">Tuner id.</param>
    /// <param name="onClose">Called once when the stream is closed (e.g. releases a manifest proxy).</param>
    public DvbILiveStream(MediaSourceInfo mediaSource, string tunerHostId, Action onClose)
    {
        _onClose = onClose;
        MediaSource = mediaSource;
        TunerHostId = tunerHostId;
        UniqueId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        ConsumerCount = 1;
    }

    /// <inheritdoc />
    public int ConsumerCount { get; set; }

    /// <inheritdoc />
    public string? OriginalStreamId { get; set; }

    /// <inheritdoc />
    public string TunerHostId { get; }

    /// <inheritdoc />
    public bool EnableStreamSharing => true;

    /// <inheritdoc />
    public MediaSourceInfo MediaSource { get; set; }

    /// <inheritdoc />
    public string UniqueId { get; }

    /// <inheritdoc />
    public Task Open(CancellationToken openCancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task Close()
    {
        Interlocked.Exchange(ref _onClose, null)?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Stream GetStream() => throw new NotSupportedException("DVB-I streams are read by ffmpeg directly from their URL.");

    /// <inheritdoc />
    public void Dispose() => Interlocked.Exchange(ref _onClose, null)?.Invoke();
}
