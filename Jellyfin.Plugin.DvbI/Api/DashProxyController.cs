using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Streaming;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api;

/// <summary>
/// Serves rewritten DASH manifests to Jellyfin's ffmpeg. ffmpeg does not authenticate, so the endpoint is
/// anonymous; it only answers for random tokens registered by the plugin for open live streams.
/// </summary>
[ApiController]
[Route("DvbI/Dash")]
[AllowAnonymous]
public class DashProxyController : ControllerBase
{
    private readonly DashManifestProxy _proxy;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashProxyController"/> class.
    /// </summary>
    /// <param name="proxy">DASH manifest proxy.</param>
    public DashProxyController(DashManifestProxy proxy)
    {
        _proxy = proxy;
    }

    /// <summary>
    /// Gets the rewritten manifest of an open live stream.
    /// </summary>
    /// <param name="token">Stream token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest.</returns>
    [HttpGet("{token}/manifest.mpd")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult> GetManifest([FromRoute] string token, CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await _proxy.GetManifestAsync(token, cancellationToken).ConfigureAwait(false);
            return manifest is null ? NotFound() : Content(manifest, "application/dash+xml");
        }
        catch (HttpRequestException)
        {
            return StatusCode(StatusCodes.Status502BadGateway);
        }
    }
}
