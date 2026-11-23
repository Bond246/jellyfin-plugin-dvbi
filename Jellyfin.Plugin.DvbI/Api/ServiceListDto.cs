using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Http;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.DvbI.Parsing;
using Jellyfin.Plugin.DvbI.ServiceLists;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.DvbI.Api;

/// <summary>
/// Service list summary.
/// </summary>
/// <param name="Name">Name.</param>
/// <param name="Provider">Provider.</param>
/// <param name="Version">Version.</param>
/// <param name="ServiceCount">All services.</param>
/// <param name="IpServiceCount">Services playable over IP.</param>
/// <param name="Regions">Flattened region tree in document order.</param>
public sealed record ServiceListDto(string Name, string Provider, string Version, int ServiceCount, int IpServiceCount, IReadOnlyList<RegionDto> Regions);
