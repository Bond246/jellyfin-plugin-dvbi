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
/// A region.
/// </summary>
/// <param name="Id">Region id.</param>
/// <param name="Name">Name.</param>
/// <param name="Depth">Depth in the tree, 0 = top.</param>
/// <param name="Selectable">Whether the region may be selected.</param>
public sealed record RegionDto(string Id, string Name, int Depth, bool Selectable);
