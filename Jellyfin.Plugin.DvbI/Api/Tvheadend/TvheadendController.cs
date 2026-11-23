using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Jellyfin.Plugin.DvbI.Configuration;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using Jellyfin.Plugin.DvbI.Tvheadend;
using MediaBrowser.Common.Api;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api.Tvheadend;

/// <summary>
/// TVHeadend endpoints of the configuration page (administrators only).
/// </summary>
[ApiController]
[Route("DvbI/Tvheadend")]
[Authorize(Policy = Policies.RequiresElevation)]
[Produces(MediaTypeNames.Application.Json)]
public class TvheadendController : ControllerBase
{
    private const string TvheadendPluginConfigFile = "TVHeadEnd.xml";

    private readonly TvhClient _tvh;
    private readonly ServiceListProvider _serviceLists;
    private readonly IApplicationPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="TvheadendController"/> class.
    /// </summary>
    /// <param name="tvh">TVHeadend client.</param>
    /// <param name="serviceLists">Service list provider.</param>
    /// <param name="paths">Application paths.</param>
    public TvheadendController(TvhClient tvh, ServiceListProvider serviceLists, IApplicationPaths paths)
    {
        _tvh = tvh;
        _serviceLists = serviceLists;
        _paths = paths;
    }

    /// <summary>
    /// Reads the connection settings of the TVHeadend plugin (jellyfin-plugin-tvheadend), if installed.
    /// </summary>
    /// <returns>The settings.</returns>
    [HttpGet("ImportFromPlugin")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<TvheadendSettingsDto> ImportFromPlugin()
    {
        var file = Path.Combine(_paths.PluginConfigurationsPath, TvheadendPluginConfigFile);
        if (!System.IO.File.Exists(file))
        {
            return NotFound();
        }

        XElement root;
        try
        {
            root = XDocument.Load(file).Root ?? throw new XmlException("empty");
        }
        catch (XmlException)
        {
            return NotFound();
        }

        string? Value(string name) => root.Element(name)?.Value.Trim() is { Length: > 0 } v ? v : null;
        var host = Value("TVH_ServerName");
        if (host is null)
        {
            return NotFound();
        }

        var url = (host.Contains("://", StringComparison.Ordinal) ? host : "http://" + host) + ":" + (Value("HTTP_Port") ?? "9981");
        // The password leaves the server only encrypted; the configuration page passes it back as is.
        return Ok(new TvheadendSettingsDto(url, Value("Username"), SecretProtector.Default.Protect(Value("Password")), Value("Profile") ?? "pass", false));
    }

    /// <summary>
    /// Tests a TVHeadend connection and shows which DVB-I services it can receive.
    /// </summary>
    /// <param name="settings">Settings to test (not saved).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Server information and matching per service list.</returns>
    [HttpPost("Test")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<TvheadendTestResultDto>> Test([FromBody] TvheadendSettingsDto settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var tvhSettings = TvhSettings.Create(settings.Url, settings.Username, settings.Password, settings.Profile);
        if (tvhSettings is null)
        {
            return BadRequest("Invalid TVHeadend URL");
        }

        try
        {
            var (version, apiVersion) = await _tvh.GetServerInfoAsync(tvhSettings, cancellationToken).ConfigureAwait(false);
            var services = await _tvh.GetServicesAsync(tvhSettings, true, cancellationToken).ConfigureAwait(false);
            var lists = new List<TvheadendListResultDto>();
            foreach (var source in Plugin.Instance?.Configuration.ServiceLists.Where(s => s.Enabled) ?? [])
            {
                lists.Add(await MatchListAsync(source.Name, source.Url, services, settings.AllowEncrypted, cancellationToken).ConfigureAwait(false));
            }

            return Ok(new TvheadendTestResultDto(version, apiVersion, services.Count, lists));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !cancellationToken.IsCancellationRequested)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private async Task<TvheadendListResultDto> MatchListAsync(string name, string url, IReadOnlyList<TvhService> services, bool allowEncrypted, CancellationToken cancellationToken)
    {
        Model.ServiceList list;
        try
        {
            list = await _serviceLists.GetServiceListAsync(url, false, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or DvbIParseException)
        {
            return new TvheadendListResultDto(name + " (" + ex.Message + ")", 0, 0, 0, [], []);
        }

        var usable = new List<string>();
        var conflicts = new List<TvheadendConflictDto>();
        var broadcastOnly = 0;
        var notReceived = 0;
        foreach (var service in list.Services)
        {
            var matches = service.Instances
                .Select(i => TvhMatcher.Match(service, i, services, allowEncrypted))
                .OfType<TvhMatch>()
                .ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            if (matches.Any(m => m.Usable))
            {
                usable.Add(service.Name);
                if (!service.Instances.Any(i => i.IsIp && !i.HasContentProtection))
                {
                    broadcastOnly++;
                }
            }
            else if (matches.All(m => m.Reason == TvhMatcher.NotFound))
            {
                notReceived++;
            }

            foreach (var match in matches.Where(m => !m.Usable && m.Reason != TvhMatcher.NotFound))
            {
                var t = TvhMatcher.Triplet(match.Instance);
                conflicts.Add(new TvheadendConflictDto(
                    service.Name,
                    match.Instance.Delivery.ToString(),
                    $"{t?.OriginalNetworkId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}.{t?.TransportStreamId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}.{t?.ServiceId.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    match.Service?.Name,
                    match.Reason ?? string.Empty));
            }
        }

        return new TvheadendListResultDto(name, usable.Count, broadcastOnly, notReceived, usable, conflicts);
    }
}
